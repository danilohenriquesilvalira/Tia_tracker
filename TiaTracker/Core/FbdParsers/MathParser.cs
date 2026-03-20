using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Math Functions (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Math function").
    ///
    /// Instruções confirmadas por XML real:
    ///   Calc — CALCULATE  (<Equation> + in1..inN, out)  ← tem elemento <Equation> especial
    ///   Add  — ADD        (in1..inN, out)               ← Card variável
    ///   Sub  — SUB        (in1, in2, out)
    ///   Mul  — MUL        (in1..inN, out)               ← Card variável
    ///   Div  — DIV        (in1, in2, out)
    ///   Mod  — MOD        (in1, in2, out)
    ///   Neg  — NEG        (in, out)                     ← porta "in", não "in1"
    ///   Abs  — ABS        (in, out)                     ← porta "in", não "in1"
    ///   Inc  — INC        (operand)                     ← in-place! sem fio de saída
    ///   Dec  — DEC        (operand)                     ← in-place! sem fio de saída
    ///
    /// Instruções pendentes de verificação via XML real (mantidas como best-guess):
    ///   Expt, Min, Max, Limit, Sqrt, Ln, Log, Exp,
    ///   Sin, Cos, Tan, Asin, Acos, Atan, Frac
    ///
    /// Estrutura XML (exemplo Add):
    ///   <Part Name="Add" UId="24" DisabledENO="false">
    ///     <TemplateValue Name="Card" Type="Cardinality">2</TemplateValue>
    ///     <TemplateValue Name="SrcType" Type="Type">Int</TemplateValue>
    ///   </Part>
    ///
    /// Estrutura XML (Calc — especial):
    ///   <Part Name="Calc" UId="24" DisabledENO="false">
    ///     <Equation>(IN1 + IN2) * (IN1 - IN2)</Equation>
    ///     <TemplateValue Name="Card" Type="Cardinality">2</TemplateValue>
    ///     <TemplateValue Name="SrcType" Type="Type">Int</TemplateValue>
    ///   </Part>
    ///
    /// Estrutura XML (Inc/Dec — especial):
    ///   <Part Name="Inc" UId="22" DisabledENO="false">
    ///     <TemplateValue Name="DestType" Type="Type">Int</TemplateValue>
    ///   </Part>
    ///   Wire: IdentCon(var) → NameCon(22,"operand")   ← sem fio de saída!
    ///
    /// Portas confirmadas (MINÚSCULAS): en, in1, in2, in, out, operand
    /// Saída "out" vai direto para IdentCon (não via Coil).
    /// CollectOutputs deste parser gera todas as linhas — 3k skip list deve incluir estes nomes.
    /// </summary>
    internal static class MathParser
    {
        // Nomes de Part reconhecidos — usados também pelo ProjectReader para skip list do 3k.
        internal static readonly HashSet<string> Handled = new HashSet<string>
        {
            // Confirmados por XML real
            "Calc", "Add", "Sub", "Mul", "Div", "Mod", "Neg", "Abs", "Inc", "Dec",
            // Pendentes de verificação (best-guess mantido)
            "Expt", "Min", "Max", "Limit",
            "Sqrt", "Ln", "Log", "Exp",
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Frac",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!Handled.Contains(name)) return null;

            var neg  = FbdContext.GetNegatedPorts(part);
            int card = FbdContext.GetCardinality(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                // ── CALCULATE — lê <Equation> e expande entradas ─────────────────
                case "Calc":
                {
                    var eq     = part.Elements().FirstOrDefault(e => e.Name.LocalName == "Equation")?.Value ?? "?";
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    var inStr  = string.Join(", ", inputs.Select((v, i) => $"IN{i + 1}:={v}"));
                    return $"CALCULATE[{eq}]({inStr})";
                }

                // ── Aritmética variável (N entradas via Card) ─────────────────────
                case "Add":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "ADD(?)" : $"({string.Join(" + ", inputs)})";
                }
                case "Mul":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "MUL(?)" : $"({string.Join(" * ", inputs)})";
                }

                // ── Aritmética binária fixa ───────────────────────────────────────
                case "Sub":  return $"({Inp("in1")} - {Inp("in2")})";
                case "Div":  return $"({Inp("in1")} / {Inp("in2")})";
                case "Mod":  return $"({Inp("in1")} MOD {Inp("in2")})";

                // ── Unário (porta "in", não "in1") ────────────────────────────────
                case "Neg":  return $"(-{Inp("in")})";
                case "Abs":  return $"ABS({Inp("in")})";

                // ── In-place (quando tracejado ao contrário, mostra operand) ──────
                case "Inc":
                {
                    var v = ctx.WireFrom.TryGetValue((uid, "operand"), out var s) ? ctx.ResolveNode(s.uid, s.port, depth) : "?";
                    return $"INC({v})";
                }
                case "Dec":
                {
                    var v = ctx.WireFrom.TryGetValue((uid, "operand"), out var s) ? ctx.ResolveNode(s.uid, s.port, depth) : "?";
                    return $"DEC({v})";
                }

                // ── Pendentes de verificação ──────────────────────────────────────
                case "Expt": return $"EXPT({Inp("in1")}, {Inp("in2")})";
                case "Min":  return $"MIN({Inp("in1")}, {Inp("in2")})";
                case "Max":  return $"MAX({Inp("in1")}, {Inp("in2")})";
                case "Limit":return $"LIMIT(MN:={Inp("MN")}, IN:={Inp("IN")}, MX:={Inp("MX")})";
                case "Sqrt": return $"SQRT({Inp("in")})";
                case "Ln":   return $"LN({Inp("in")})";
                case "Log":  return $"LOG({Inp("in")})";
                case "Exp":  return $"EXP({Inp("in")})";
                case "Sin":  return $"SIN({Inp("in")})";
                case "Cos":  return $"COS({Inp("in")})";
                case "Tan":  return $"TAN({Inp("in")})";
                case "Asin": return $"ASIN({Inp("in")})";
                case "Acos": return $"ACOS({Inp("in")})";
                case "Atan": return $"ATAN({Inp("in")})";
                case "Frac": return $"FRAC({Inp("in")})";

                default: return null;
            }
        }

        /// <summary>
        /// Coleta saídas de todas as instruções matemáticas.
        ///   out  → IdentCon : gera "var := expr"
        ///   Inc/Dec          : gera "INC(var)" / "DEC(var)" via porta operand
        /// </summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!Handled.Contains(name)) continue;

                // ── Inc / Dec: in-place, sem fio de saída ─────────────────────────
                if (name == "Inc" || name == "Dec")
                {
                    if (!ctx.WireFrom.TryGetValue((uid, "operand"), out var opSrc)) continue;
                    if (!ctx.AccessMap.ContainsKey(opSrc.uid)) continue;
                    var varName = ctx.GetVarName(ctx.AccessMap[opSrc.uid]);
                    result.Add(name == "Inc" ? $"INC({varName})" : $"DEC({varName})");
                    continue;
                }

                // ── out → IdentCon ────────────────────────────────────────────────
                if (!ctx.WireTo.TryGetValue((uid, "out"), out var outDsts)) continue;
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
