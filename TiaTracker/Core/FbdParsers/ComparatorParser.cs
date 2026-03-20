using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Comparator Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: verificar portas exatas via XML real.
    ///
    /// Instruções a suportar:
    ///   CmpEQ   — Igual         (in1, in2 → out bool)
    ///   CmpNE   — Diferente     (in1, in2 → out bool)
    ///   CmpGT   — Maior que     (in1, in2 → out bool)
    ///   CmpGE   — Maior ou igual(in1, in2 → out bool)
    ///   CmpLT   — Menor que     (in1, in2 → out bool)
    ///   CmpLE   — Menor ou igual(in1, in2 → out bool)
    ///   InRange  — IN_RANGE     (MIN, VAL, MAX → out bool)
    ///   OutRange — OUT_RANGE    (MIN, VAL, MAX → out bool)
    ///   IsValid  — OK           (in → out bool)
    ///   IsInvalid— NOT_OK       (in → out bool)
    /// </summary>
    internal static class ComparatorParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "CmpEQ", "CmpNE", "CmpGT", "CmpGE", "CmpLT", "CmpLE",
            "EQ", "NE", "GT", "GE", "LT", "LE",  // nomes alternativos
            "InRange", "OutRange", "IsValid", "IsInvalid", "Cmp",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "CmpEQ": case "EQ":  return $"({Inp("in1")} = {Inp("in2")})";
                case "CmpNE": case "NE":  return $"({Inp("in1")} <> {Inp("in2")})";
                case "CmpGT": case "GT":  return $"({Inp("in1")} > {Inp("in2")})";
                case "CmpGE": case "GE":  return $"({Inp("in1")} >= {Inp("in2")})";
                case "CmpLT": case "LT":  return $"({Inp("in1")} < {Inp("in2")})";
                case "CmpLE": case "LE":  return $"({Inp("in1")} <= {Inp("in2")})";
                case "Cmp":
                {
                    var a = ctx.WireFrom.ContainsKey((uid, "in1")) ? Inp("in1") : Inp("IN1");
                    var b = ctx.WireFrom.ContainsKey((uid, "in2")) ? Inp("in2") : Inp("IN2");
                    return $"CMP({a}, {b})";
                }
                case "InRange":   return $"IN_RANGE(MIN:={Inp("MIN")}, VAL:={Inp("VAL")}, MAX:={Inp("MAX")})";
                case "OutRange":  return $"OUT_RANGE(MIN:={Inp("MIN")}, VAL:={Inp("VAL")}, MAX:={Inp("MAX")})";
                case "IsValid":   return $"IS_VALID({Inp("in")})";
                case "IsInvalid": return $"NOT IS_VALID({Inp("in")})";
                default:          return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            // Comparadores normalmente alimentam Coil via "out" → coberto por BitLogicParser
            // Nada adicional a colectar aqui por enquanto
        }
    }
}
