using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Program Control / Misc Operations (FBD/LAD) — S7-1200/S7-1500.
    ///
    /// Instruções suportadas:
    ///   Calculate    — Caixa de expressão (múltiplas entradas → out)
    ///   TypeConvert  — Conversão com tipos explícitos (srcType, dstType, in → out)
    ///   CONV         — alias TypeConvert
    /// </summary>
    internal static class ProgramControlParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "Calculate", "CALCULATE",
            "TypeConvert", "CONV",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Calculate":
                case "CALCULATE":
                {
                    var exprVal = FbdContext.GetTemplateValue(part, "Expression") ?? "?";
                    // Collect all connected input ports
                    var calcInputs = ctx.WireFrom.Keys
                        .Where(k => k.uid == uid)
                        .OrderBy(k => k.port)
                        .Select(k =>
                        {
                            var s = ctx.WireFrom[k];
                            var v = ctx.ResolveNode(s.uid, s.port, depth);
                            return $"{k.port}:={v}";
                        });
                    return $"CALCULATE({exprVal}; {string.Join(", ", calcInputs)})";
                }

                case "TypeConvert":
                case "CONV":
                {
                    var srcType = FbdContext.GetTemplateValue(part, "srcType") ?? "";
                    var dstType = FbdContext.GetTemplateValue(part, "dstType") ?? "";
                    return string.IsNullOrEmpty(srcType)
                        ? $"CONVERT({Inp("in")})"
                        : $"{srcType}_TO_{dstType}({Inp("in")})";
                }


                default: return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            // Para Calculate/TypeConvert: saída "out" ou portas numeradas
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                // EN condition
                string enCond = "";
                if (ctx.WireFrom.TryGetValue((uid, "en"), out var enSrc) &&
                    enSrc.uid != "PWR" && enSrc.uid != "OPEN")
                    enCond = ctx.ResolveNode(enSrc.uid, enSrc.port, 0);
                string prefix = string.IsNullOrEmpty(enCond) ? "" : $"IF {enCond}: ";

                // Saída "out"
                if (ctx.WireTo.TryGetValue((uid, "out"), out var outDsts))
                {
                    var expr = Resolve(uid, part, ctx, 0);
                    if (expr == null) continue;
                    foreach (var dst in outDsts)
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                            result.Add($"{prefix}{ctx.ResolveDestination(dst.uid, dst.port)} := {expr}");
                }


            }
        }
    }
}
