using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de String Operations (FBD/LAD) — S7-1200/S7-1500.
    ///
    /// Instruções suportadas:
    ///   Concat  — Concatenar      (IN1, IN2 → out)
    ///   Left    — Esquerda        (IN, L → out)
    ///   Right   — Direita         (IN, L → out)
    ///   Mid     — Meio            (IN, L, P → out)
    ///   Len     — Comprimento     (IN → out)
    ///   Find    — Procurar        (IN1, IN2 → out)
    ///   Replace — Substituir      (IN, IN1, L, P → out)
    ///   Insert  — Inserir         (IN, IN1, P → out)
    ///   Delete  — Apagar          (IN, L, P → out)
    /// </summary>
    internal static class StringParser
    {
        private static readonly System.Collections.Generic.HashSet<string> _handled
            = new System.Collections.Generic.HashSet<string>
        {
            "Concat", "Left", "Right", "Mid", "Len",
            "Find", "Replace", "Insert", "Delete",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Concat":  return $"CONCAT({Inp("IN1")}, {Inp("IN2")})";
                case "Left":    return $"LEFT(IN:={Inp("IN")}, L:={Inp("L")})";
                case "Right":   return $"RIGHT(IN:={Inp("IN")}, L:={Inp("L")})";
                case "Mid":     return $"MID(IN:={Inp("IN")}, L:={Inp("L")}, P:={Inp("P")})";
                case "Len":     return $"LEN({Inp("IN")})";
                case "Find":    return $"FIND(IN1:={Inp("IN1")}, IN2:={Inp("IN2")})";
                case "Replace": return $"REPLACE(IN:={Inp("IN")}, IN1:={Inp("IN1")}, L:={Inp("L")}, P:={Inp("P")})";
                case "Insert":  return $"INSERT(IN:={Inp("IN")}, IN1:={Inp("IN1")}, P:={Inp("P")})";
                case "Delete":  return $"DELETE(IN:={Inp("IN")}, L:={Inp("L")}, P:={Inp("P")})";
                default:        return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, System.Collections.Generic.List<string> result)
        {
            // String ops alimentam variável via "out" → IdentCon destino
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
