using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Timer Operations (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Timer operations").
    ///
    /// Instruções suportadas:
    ///   TP   — Generate pulse       (IN, PT → Q, ET)
    ///   TON  — Generate on-delay    (IN, PT → Q, ET)
    ///   TOF  — Generate off-delay   (IN, PT → Q, ET)
    ///   TONR — Time accumulator     (IN, R, PT → Q, ET)
    ///
    /// Estrutura XML do Part (exemplo TP):
    ///   <Part Name="TP" Version="1.0" UId="25">
    ///     <Instance Scope="GlobalVariable" UId="26">
    ///       <Component Name="IEC_Timer_0_DB"/>
    ///     </Instance>
    ///     <TemplateValue Name="time_type" Type="Type">Time</TemplateValue>
    ///   </Part>
    ///
    /// Portas confirmadas (case-sensitive, MAIÚSCULAS):
    ///   Entradas: IN, PT  (+ R para TONR)
    ///   Saídas:   Q (→ Coil.in, tratado por BitLogicParser), ET (→ IdentCon direto)
    ///
    /// Fluxo de processamento:
    ///   Q → Coil: BitLogicParser.CollectCoil resolve Coil.in → ResolveNode(timerUid,"Q")
    ///             → Dispatch → TimerParser.Resolve() retorna expressão de chamada inline.
    ///   ET → variável: TimerParser.CollectOutputs gera "etVar := inst.ET".
    /// </summary>
    internal static class TimerParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "TP", "TON", "TOF", "TONR",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var inst = FbdContext.GetPartInstanceName(part) ?? name;
            var neg  = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "TP":   return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TON":  return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TOF":  return $"{inst}(IN:={Inp("IN")}, PT:={Inp("PT")})";
                case "TONR": return $"{inst}(IN:={Inp("IN")}, R:={Inp("R")}, PT:={Inp("PT")})";
                default:     return null;
            }
        }

        /// <summary>
        /// Coleta saída ET de timers (Q → Coil já é tratado por BitLogicParser).
        /// Wire: NameCon(timerUid,"ET") → IdentCon(accessUid) gera "etVar := inst.ET".
        /// </summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                var inst = FbdContext.GetPartInstanceName(part) ?? name;

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
            }
        }
    }
}
