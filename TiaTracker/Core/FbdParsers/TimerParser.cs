using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Timer Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: implementar após receber XML real de cada timer.
    ///
    /// Instruções a suportar:
    ///   TON  — Timer On-Delay   (IN, PT → Q, ET)
    ///   TOF  — Timer Off-Delay  (IN, PT → Q, ET)
    ///   TP   — Timer Pulse      (IN, PT → Q, ET)
    ///   TONR — Timer On-Delay Retentive (IN, R, PT → Q, ET)
    ///
    /// Todas as timers têm elemento <Instance> para o nome da variável de instância.
    /// Portas confirmadas no TIA Portal: IN, PT, R (TONR), Q, ET — verificar case no XML.
    /// </summary>
    internal static class TimerParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "TON", "TOF", "TP", "TONR",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var inst   = FbdContext.GetPartInstanceName(part) ?? name;
            var neg    = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "TON":  return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TOF":  return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TP":   return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TONR": return $"{inst}(IN:={Inp("IN")}, R:={Inp("R")}, PT:={Inp("PT")})";
                default:     return null;
            }
        }

        /// <summary>Coleta saídas de timers (Q → coil, ET → variável).</summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            // Timers normalmente têm Q saindo para Coil (já coberto por BitLogicParser)
            // e ET saindo para uma variável. ET que termina em IdentCon é capturado aqui.
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                var inst = FbdContext.GetPartInstanceName(part) ?? name;

                // ET saída → variável
                if (ctx.WireTo.TryGetValue((uid, "ET"), out var etDsts))
                {
                    foreach (var dst in etDsts)
                    {
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                        {
                            var varName = ctx.ResolveDestination(dst.uid, dst.port);
                            result.Add($"{varName} := {inst}.ET");
                        }
                    }
                }

                // CV saída (se existir) → variável
                if (ctx.WireTo.TryGetValue((uid, "CV"), out var cvDsts))
                {
                    foreach (var dst in cvDsts)
                    {
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                        {
                            var varName = ctx.ResolveDestination(dst.uid, dst.port);
                            result.Add($"{varName} := {inst}.CV");
                        }
                    }
                }
            }
        }
    }
}
