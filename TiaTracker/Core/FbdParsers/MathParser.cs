using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Math Functions (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: verificar portas exatas via XML real.
    ///
    /// Instruções a suportar:
    ///   Add, Sub, Mul, Div, Mod — Aritmética básica (in1, in2, ..., out)
    ///   Neg, Abs                 — Negação / Valor absoluto (in, out)
    ///   Expt                     — Potência (in1, in2, out)
    ///   Sqrt, Ln, Log, Exp       — Funções matemáticas (in, out)
    ///   Sin, Cos, Tan, Asin, Acos, Atan, Frac — Trigonometria (in, out)
    ///   Min, Max                 — Mínimo/Máximo (in1, in2, ..., out)
    ///   Limit                    — Limitar (MN, IN, MX → out)
    /// </summary>
    internal static class MathParser
    {
        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            var neg  = FbdContext.GetNegatedPorts(part);
            int card = FbdContext.GetCardinality(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Add":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "ADD(?)" : $"({string.Join(" + ", inputs)})";
                }
                case "Sub":   return $"({Inp("in1")} - {Inp("in2")})";
                case "Mul":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "MUL(?)" : $"({string.Join(" * ", inputs)})";
                }
                case "Div":   return $"({Inp("in1")} / {Inp("in2")})";
                case "Mod":   return $"({Inp("in1")} MOD {Inp("in2")})";
                case "Neg":   return $"(-{Inp("in")})";
                case "Abs":   return $"ABS({Inp("in")})";
                case "Expt":  return $"EXPT({Inp("in1")}, {Inp("in2")})";
                case "Min":   return $"MIN({Inp("in1")}, {Inp("in2")})";
                case "Max":   return $"MAX({Inp("in1")}, {Inp("in2")})";
                case "Limit": return $"LIMIT(MN:={Inp("MN")}, IN:={Inp("IN")}, MX:={Inp("MX")})";
                case "Sqrt":  return $"SQRT({Inp("in")})";
                case "Ln":    return $"LN({Inp("in")})";
                case "Log":   return $"LOG({Inp("in")})";
                case "Exp":   return $"EXP({Inp("in")})";
                case "Sin":   return $"SIN({Inp("in")})";
                case "Cos":   return $"COS({Inp("in")})";
                case "Tan":   return $"TAN({Inp("in")})";
                case "Asin":  return $"ASIN({Inp("in")})";
                case "Acos":  return $"ACOS({Inp("in")})";
                case "Atan":  return $"ATAN({Inp("in")})";
                case "Frac":  return $"FRAC({Inp("in")})";
                default:      return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, System.Collections.Generic.List<string> result)
        {
            // Math normalmente alimenta Move ou variável diretamente via IdentCon destino
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";

                // Verificar se a saída "out" vai para um IdentCon (variável)
                if (ctx.WireTo.TryGetValue((uid, "out"), out var outDsts))
                {
                    foreach (var dst in outDsts)
                    {
                        if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                        var expr    = Resolve(uid, part, ctx, 0);
                        if (expr == null) continue;
                        var varName = ctx.ResolveDestination(dst.uid, dst.port);
                        result.Add($"{varName} := {expr}");
                    }
                }
            }
        }
    }
}
