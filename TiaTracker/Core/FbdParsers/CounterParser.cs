using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Counter Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: implementar após receber XML real de cada counter.
    ///
    /// Instruções a suportar:
    ///   CTU  — Counter Up       (CU, R, PV → Q, CV)
    ///   CTD  — Counter Down     (CD, LD, PV → Q, CV)
    ///   CTUD — Counter Up/Down  (CU, CD, R, LD, PV → QU, QD, CV)
    ///
    /// Todas têm elemento <Instance> para variável de instância.
    /// Confirmar portas maiúsculas/minúsculas via XML real.
    /// </summary>
    internal static class CounterParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "CTU", "CTD", "CTUD",
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
                case "CTU":  return $"{inst}(CU:={Inp("CU")}, R:={Inp("R")}, PV:={Inp("PV")})";
                case "CTD":  return $"{inst}(CD:={Inp("CD")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                case "CTUD": return $"{inst}(CU:={Inp("CU")}, CD:={Inp("CD")}, R:={Inp("R")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                default:     return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            // CV saída → variável (QU/QD normalmente vão para Coil — coberto por BitLogicParser)
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                var inst = FbdContext.GetPartInstanceName(part) ?? name;

                if (ctx.WireTo.TryGetValue((uid, "CV"), out var cvDsts))
                    foreach (var dst in cvDsts)
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                            result.Add($"{ctx.ResolveDestination(dst.uid, dst.port)} := {inst}.CV");
            }
        }
    }
}
