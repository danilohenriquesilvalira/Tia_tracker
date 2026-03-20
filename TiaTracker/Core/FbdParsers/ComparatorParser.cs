using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Comparator Operations (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Comparator operation").
    ///
    /// Instruções confirmadas por XML real:
    ///   Eq  — Equal           (in1, in2 → out)   CMP ==
    ///   Ne  — Not equal       (in1, in2 → out)   CMP <>
    ///   Ge  — Greater/equal   (in1, in2 → out)   CMP >=
    ///   Le  — Less/equal      (in1, in2 → out)   CMP <=
    ///   Gt  — Greater than    (in1, in2 → out)   CMP >
    ///   Lt  — Less than       (in1, in2 → out)   CMP <
    ///
    /// Instruções pendentes de verificação via XML real:
    ///   InRange   — IN_RANGE   (MIN, VAL, MAX → out)
    ///   OutRange  — OUT_RANGE  (MIN, VAL, MAX → out)
    ///   IsValid   — IS_VALID   (in → out)
    ///   IsInvalid — NOT IS_VALID (in → out)
    ///
    /// Estrutura XML do Part (exemplo Eq):
    ///   <Part Name="Eq" UId="24">
    ///     <TemplateValue Name="SrcType" Type="Type">Int</TemplateValue>
    ///   </Part>
    ///
    /// Portas confirmadas (MINÚSCULAS): in1, in2, out
    /// Sem <Instance> — são funções puras, não FBs.
    /// Saída "out" vai sempre para Coil.in → coberto por BitLogicParser.CollectOutputs.
    /// CollectOutputs deste parser não precisa fazer nada.
    ///
    /// Suporta todos os data types do TIA Portal:
    ///   Bool, Byte, Word, DWord, LWord,
    ///   SInt, Int, DInt, LInt, USInt, UInt, UDInt, ULInt,
    ///   Real, LReal, Time, LTime, Date, DTL, String, WString, Char, WChar, etc.
    ///   (o TemplateValue SrcType indica o tipo selecionado — não afeta o parsing das portas)
    /// </summary>
    internal static class ComparatorParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            // Confirmados por XML real
            "Eq", "Ne", "Ge", "Le", "Gt", "Lt",
            // Pendentes de verificação
            "InRange", "OutRange", "IsValid", "IsInvalid",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                // ── Comparadores binários confirmados ─────────────────────────────
                case "Eq": return $"({Inp("in1")} = {Inp("in2")})";
                case "Ne": return $"({Inp("in1")} <> {Inp("in2")})";
                case "Ge": return $"({Inp("in1")} >= {Inp("in2")})";
                case "Le": return $"({Inp("in1")} <= {Inp("in2")})";
                case "Gt": return $"({Inp("in1")} > {Inp("in2")})";
                case "Lt": return $"({Inp("in1")} < {Inp("in2")})";

                // ── Pendentes de verificação via XML real ─────────────────────────
                case "InRange":   return $"IN_RANGE(MIN:={Inp("MIN")}, VAL:={Inp("VAL")}, MAX:={Inp("MAX")})";
                case "OutRange":  return $"OUT_RANGE(MIN:={Inp("MIN")}, VAL:={Inp("VAL")}, MAX:={Inp("MAX")})";
                case "IsValid":   return $"IS_VALID({Inp("in")})";
                case "IsInvalid": return $"NOT IS_VALID({Inp("in")})";

                default: return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            // Comparadores alimentam Coil via porta "out" — coberto por BitLogicParser.
            // Saída direta para IdentCon é tratada pelo bloco 3k do ProjectReader.
        }
    }
}
