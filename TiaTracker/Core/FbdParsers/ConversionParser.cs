using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Conversion Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: verificar portas exatas via XML real.
    ///
    /// Instruções a suportar:
    ///   Convert   — CONV tipo-a-tipo (en, in → eno, out)
    ///   Round     — ROUND           (en, in → eno, out)
    ///   Trunc     — TRUNC           (en, in → eno, out)
    ///   Ceiling   — CEIL            (en, in → eno, out)
    ///   Floor     — FLOOR           (en, in → eno, out)
    ///   Scale     — SCALE_X         (en, VALUE, MIN, MAX → eno, out)
    ///   Normalize — NORM_X          (en, VALUE, MIN, MAX → eno, out)
    /// </summary>
    internal static class ConversionParser
    {
        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            var neg  = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Convert":   return $"CONVERT({Inp("in")})";
                case "Round":     return $"ROUND({Inp("in")})";
                case "Trunc":     return $"TRUNC({Inp("in")})";
                case "Ceiling":   return $"CEIL({Inp("in")})";
                case "Floor":     return $"FLOOR({Inp("in")})";
                case "Scale":     return $"SCALE_X(VALUE:={Inp("VALUE")}, MIN:={Inp("MIN")}, MAX:={Inp("MAX")})";
                case "Normalize": return $"NORM_X(VALUE:={Inp("VALUE")}, MIN:={Inp("MIN")}, MAX:={Inp("MAX")})";
                default:          return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, System.Collections.Generic.List<string> result)
        {
            // Conversões alimentam variável via "out" → IdentCon destino
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
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
