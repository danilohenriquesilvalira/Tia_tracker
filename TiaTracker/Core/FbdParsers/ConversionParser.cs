using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Conversion Operations (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Conversion operation").
    ///
    /// Instruções confirmadas por XML real:
    ///   Convert   — CONVERT  (in, out)  TemplateValue SrcType + DestType (ex: Int→Real)
    ///   Round     — ROUND    (in, out)
    ///   Ceil      — CEIL     (in, out)  ← Part Name "Ceil", NÃO "Ceiling"
    ///   Floor     — FLOOR    (in, out)
    ///   Trunc     — TRUNC    (in, out)
    ///   Scale_X   — SCALE_X  (min, value, max, out)  ← Part Name "Scale_X"
    ///   Normalize — NORM_X   (min, value, max, out)  ← Part Name "Normalize" (NÃO "Norm_X"!)
    ///
    /// Estrutura XML (exemplo Convert Int→Real):
    ///   <Part Name="Convert" UId="23" DisabledENO="false">
    ///     <TemplateValue Name="SrcType"  Type="Type">Int</TemplateValue>
    ///     <TemplateValue Name="DestType" Type="Type">Real</TemplateValue>
    ///   </Part>
    ///
    /// Portas confirmadas (MINÚSCULAS):
    ///   Convert/Round/Ceil/Floor/Trunc : in, out
    ///   Scale_X / Normalize            : min, value, max, out
    ///
    /// Saída "out" vai direto para IdentCon (não via Coil).
    /// CollectOutputs deste parser gera todas as linhas — Handled usado pelo ProjectReader para
    /// evitar duplicatas com o bloco 3k.
    /// </summary>
    internal static class ConversionParser
    {
        internal static readonly HashSet<string> Handled = new HashSet<string>
        {
            "Convert", "Round", "Ceil", "Floor", "Trunc", "Scale_X", "Normalize",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!Handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Convert":
                {
                    var src  = FbdContext.GetTemplateValue(part, "SrcType")  ?? "?";
                    var dest = FbdContext.GetTemplateValue(part, "DestType") ?? "?";
                    return $"CONVERT({src}→{dest}: {Inp("in")})";
                }
                case "Round":     return $"ROUND({Inp("in")})";
                case "Ceil":      return $"CEIL({Inp("in")})";
                case "Floor":     return $"FLOOR({Inp("in")})";
                case "Trunc":     return $"TRUNC({Inp("in")})";
                case "Scale_X":   return $"SCALE_X(MIN:={Inp("min")}, VALUE:={Inp("value")}, MAX:={Inp("max")})";
                case "Normalize": return $"NORM_X(MIN:={Inp("min")}, VALUE:={Inp("value")}, MAX:={Inp("max")})";
                default:          return null;
            }
        }

        /// <summary>
        /// Coleta saída "out" → IdentCon para todas as instruções de conversão.
        /// </summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!Handled.Contains(name)) continue;

                if (!ctx.WireTo.TryGetValue((uid, "out"), out var outDsts)) continue;
                var expr = Resolve(uid, part, ctx, 0);
                if (expr == null) continue;

                foreach (var dst in outDsts)
                    if (ctx.AccessMap.ContainsKey(dst.uid))
                        result.Add($"{ctx.ResolveDestination(dst.uid, dst.port)} := {expr}");
            }
        }
    }
}
