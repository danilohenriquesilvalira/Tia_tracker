using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Word Logic + Shift/Rotate Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: verificar portas exatas via XML real.
    ///
    /// ATENÇÃO: No TIA Portal XML, as word logic usam nomes DIFERENTES das bit logic:
    ///   Bit logic AND gate → Part Name="A"
    ///   Word logic AND     → Part Name="And"  (maiúscula, diferente!)
    ///   Bit logic OR gate  → Part Name="O"
    ///   Word logic OR      → Part Name="Or"
    ///
    /// Instruções a suportar:
    ///   And  — AND palavra    (en, in1, in2, ..., out)
    ///   Or   — OR palavra     (en, in1, in2, ..., out)
    ///   Xor  — XOR palavra    (en, in1, in2, ..., out)
    ///   Inv  — Complemento    (en, in → eno, out)
    ///   Deco — DECO           (en, in → eno, out)
    ///   Enco — ENCO           (en, in → eno, out)
    ///   Sel  — SEL            (en, G, IN0, IN1 → eno, out)
    ///   Mux  — MUX            (en, K, IN0, IN1, ... → eno, out)
    ///   Demux— DEMUX          (en, K, in, ELSE → eno, OUT0, OUT1, ...)
    ///   Shl  — SHL shift left (en, in, N → eno, out)
    ///   Shr  — SHR shift right(en, in, N → eno, out)
    ///   Rol  — ROL rotate left(en, in, N → eno, out)
    ///   Ror  — ROR rotate right(en, in, N → eno, out)
    /// </summary>
    internal static class WordLogicParser
    {
        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            var neg  = FbdContext.GetNegatedPorts(part);
            int card = FbdContext.GetCardinality(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "And":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "AND_W(?)" : $"({string.Join(" AND ", inputs)})";
                }
                case "Or":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "OR_W(?)" : $"({string.Join(" OR ", inputs)})";
                }
                case "Xor":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "XOR_W(?)" : $"({string.Join(" XOR ", inputs)})";
                }
                case "Inv":   return $"NOT({Inp("in")})";
                case "Deco":  return $"DECO({Inp("in")})";
                case "Enco":  return $"ENCO({Inp("in")})";
                case "Sel":   return $"SEL(G:={Inp("G")}, IN0:={Inp("IN0")}, IN1:={Inp("IN1")})";
                case "Mux":   return $"MUX(K:={Inp("K")}, IN0:={Inp("IN0")}, IN1:={Inp("IN1")})";
                case "Demux": return $"DEMUX(K:={Inp("K")}, in:={Inp("in")})";
                case "Shl":   return $"SHL({Inp("in")}, N:={Inp("N")})";
                case "Shr":   return $"SHR({Inp("in")}, N:={Inp("N")})";
                case "Rol":   return $"ROL({Inp("in")}, N:={Inp("N")})";
                case "Ror":   return $"ROR({Inp("in")}, N:={Inp("N")})";
                default:      return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, System.Collections.Generic.List<string> result)
        {
            // Word logic alimenta variável via "out" → IdentCon destino
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
