using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Bit Logic Operations (FBD/LAD) — S7-1200/S7-1500.
    ///
    /// Instruções suportadas (confirmadas por XML real exportado do TIA Portal V18):
    ///   A        — AND gate            (in1, in2, ..., out)
    ///   O        — OR gate             (in1, in2, ..., out)
    ///   X        — XOR gate            (in1, in2, ..., out)
    ///   NOT      — NOT/invert          (in → out)
    ///   Contact  — Contato NO/NF LAD   (in, operand → out)
    ///   PContact — Scan pos. edge      (bit, operand → out)   ← porta "bit", NÃO "in"!
    ///   NContact — Scan neg. edge      (bit, operand → out)   ← porta "bit", NÃO "in"!
    ///   Coil     — Bobina = / /=       (in, operand → out)
    ///   SCoil    — Set output S        (in, operand → out)
    ///   RCoil    — Reset output R      (in, operand → out)
    ///   PCoil    — Set on pos edge P=  (in, bit, operand → out)  ← porta extra "bit" (memória)
    ///   NCoil    — Set on neg edge N=  (in, bit, operand → out)  ← porta extra "bit" (memória)
    ///   Sr       — SR flip-flop        (s, r1, operand → q)
    ///   Rs       — RS flip-flop        (s1, r, operand → q)
    ///   PBox     — P_TRIG box          (in, bit → out)            ← porta "bit" (memória RLO)
    ///   NBox     — N_TRIG box          (in, bit → out)            ← porta "bit" (memória RLO)
    ///   FP       — alias PBox LAD
    ///   FN       — alias NBox LAD
    ///   R_TRIG   — Detect pos edge FB  (CLK → Q)  Part com Instance
    ///   F_TRIG   — Detect neg edge FB  (CLK → Q)  Part com Instance
    ///   SET_BF   — Set bit field       (S_BIT, COUNT → out)
    ///   RESET_BF — Reset bit field     (S_BIT, COUNT → out)
    ///
    /// ATENÇÃO: Portas de Sr/Rs são MINÚSCULAS: s, r1, s1, r, operand, q
    /// ATENÇÃO: PContact/NContact usam "bit" (não "in") para o sinal de entrada.
    /// ATENÇÃO: PBox/NBox usam "bit" para o bit de memória da borda.
    /// </summary>
    internal static class BitLogicParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "A", "O", "X", "NOT",
            "Contact", "PContact", "NContact",
            "Coil", "SCoil", "RCoil", "PCoil", "NCoil",
            "Sr", "Rs",
            "PBox", "NBox", "FP", "FN",
            "R_TRIG", "F_TRIG",
            "SET_BF", "RESET_BF",
        };

        // ── Resolução de expressão (Part usado como valor intermediário) ───────
        /// <summary>
        /// Retorna a expressão do Part, ou null se este parser não reconhece o Part Name.
        /// </summary>
        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg  = FbdContext.GetNegatedPorts(part);
            int card = FbdContext.GetCardinality(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                // ── Portas lógicas booleanas ──────────────────────────────────
                case "A":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    if (inputs.Count == 0) return "AND(?)";
                    // Adiciona parênteses se sub-expressão contém OR/XOR
                    for (int i = 0; i < inputs.Count; i++)
                        if (inputs[i].Contains(" OR ") || inputs[i].Contains(" XOR "))
                            inputs[i] = $"({inputs[i]})";
                    return string.Join(" AND ", inputs);
                }
                case "O":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "OR(?)" : string.Join(" OR ", inputs);
                }
                case "X":
                {
                    var inputs = ctx.CollectInputs(uid, card, neg, depth);
                    return inputs.Count == 0 ? "XOR(?)" : string.Join(" XOR ", inputs);
                }
                case "NOT":
                    return $"NOT({Inp("in")})";

                // ── Contatos LAD/FBD ──────────────────────────────────────────
                case "Contact":
                case "PContact":
                case "NContact":
                    return ResolveContact(uid, part, name, ctx, neg, depth);

                // ── Bobinas — quando usadas como valor intermediário (Q saída) ─
                case "Coil": case "SCoil": case "RCoil": case "PCoil": case "NCoil":
                {
                    if (ctx.WireFrom.TryGetValue((uid, "operand"), out var opSrc))
                        return ctx.ResolveNode(opSrc.uid, opSrc.port, depth);
                    return "[Coil:?]";
                }

                // ── SR flip-flop — Q saída retorna valor do operand ───────────
                case "Sr":
                {
                    if (ctx.WireFrom.TryGetValue((uid, "operand"), out var srOp))
                        return ctx.ResolveNode(srOp.uid, srOp.port, depth);
                    return $"SR(S:={Inp("s")}, R1:={Inp("r1")}).Q";
                }
                case "Rs":
                {
                    if (ctx.WireFrom.TryGetValue((uid, "operand"), out var rsOp))
                        return ctx.ResolveNode(rsOp.uid, rsOp.port, depth);
                    return $"RS(S1:={Inp("s1")}, R:={Inp("r")}).Q";
                }

                // ── Detecção de borda — box (PBox/NBox têm "bit" como memória) ─
                case "PBox": case "FP":
                {
                    // "bit" = memória de borda (armazena estado anterior)
                    var mem = ctx.WireFrom.ContainsKey((uid, "bit")) ? $", MEM:={Inp("bit")}" : "";
                    return $"P_TRIG(IN:={Inp("in")}{mem})";
                }
                case "NBox": case "FN":
                {
                    var mem = ctx.WireFrom.ContainsKey((uid, "bit")) ? $", MEM:={Inp("bit")}" : "";
                    return $"N_TRIG(IN:={Inp("in")}{mem})";
                }

                // ── R_TRIG / F_TRIG — Part com Instance (FB inline) ──────────
                // Portas: CLK (entrada), Q (saída para IdentCon), en (OpenCon)
                case "R_TRIG":
                {
                    var inst = FbdContext.GetPartInstanceName(part) ?? "R_TRIG_inst";
                    return $"{inst}.Q  // R_TRIG(CLK:={Inp("CLK")})";
                }
                case "F_TRIG":
                {
                    var inst = FbdContext.GetPartInstanceName(part) ?? "F_TRIG_inst";
                    return $"{inst}.Q  // F_TRIG(CLK:={Inp("CLK")})";
                }

                // ── SET_BF / RESET_BF — Set/Reset bit field ──────────────────
                // Portas: S_BIT (start bit), COUNT (count) → out
                case "SET_BF":   return $"SET_BF(S_BIT:={Inp("S_BIT")}, COUNT:={Inp("COUNT")})";
                case "RESET_BF": return $"RESET_BF(S_BIT:={Inp("S_BIT")}, COUNT:={Inp("COUNT")})";

                default: return null;
            }
        }

        // ── Coleta de saídas (gera linhas de output da rede) ──────────────────
        /// <summary>
        /// Percorre o PartMap, encontra todos os elementos de saída de bit logic
        /// (bobinas e flip-flops) e adiciona as linhas correspondentes ao result.
        /// </summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";

                switch (name)
                {
                    case "Coil":
                    case "SCoil":
                    case "RCoil":
                    case "PCoil":
                    case "NCoil":
                        CollectCoil(uid, part, name, ctx, result);
                        break;

                    case "Sr":
                        CollectSr(uid, ctx, result);
                        break;

                    case "Rs":
                        CollectRs(uid, ctx, result);
                        break;

                    // R_TRIG / F_TRIG: saída Q vai direto para IdentCon (variável)
                    case "R_TRIG":
                    case "F_TRIG":
                        CollectRtrig(uid, part, name, ctx, result);
                        break;
                }
            }
        }

        // ── Resolução de contato LAD/FBD ──────────────────────────────────────
        private static string ResolveContact(
            string uid, XElement part, string name,
            FbdContext ctx, HashSet<string> neg, int depth)
        {
            // PContact/NContact: sinal em "bit", memória de borda em "operand"
            // Contact normal:    variável em "operand", sinal serial em "in"
            bool isEdgeContact = name == "PContact" || name == "NContact";

            string signalPort   = isEdgeContact ? "bit"     : "operand";
            string serialPort   = isEdgeContact ? "in"      : "in";      // serial LAD chain
            string memPort      = isEdgeContact ? "operand" : null;

            if (!ctx.WireFrom.TryGetValue((uid, signalPort), out var src)) return $"{name}(?)";
            var v = ctx.ResolveNode(src.uid, src.port, depth);

            bool isNc = !isEdgeContact &&
                        (part.Attribute("Negated")?.Value == "true" || neg.Contains("operand"));

            string expr;
            if (name == "PContact")
            {
                // Mostra sinal + memória de borda
                string mem = "";
                if (memPort != null && ctx.WireFrom.TryGetValue((uid, memPort), out var ms))
                    mem = $", MEM:={ctx.ResolveNode(ms.uid, ms.port, depth)}";
                expr = $"P_EDGE({v}{mem})";
            }
            else if (name == "NContact")
            {
                string mem = "";
                if (memPort != null && ctx.WireFrom.TryGetValue((uid, memPort), out var ms))
                    mem = $", MEM:={ctx.ResolveNode(ms.uid, ms.port, depth)}";
                expr = $"N_EDGE({v}{mem})";
            }
            else
            {
                expr = isNc ? $"NOT({v})" : v;
            }

            // LAD série: se "in" não vem de PowerRail/OpenCon, encadeia AND
            if (ctx.WireFrom.TryGetValue((uid, serialPort), out var inSrc) &&
                inSrc.uid != "PWR" && inSrc.uid != "OPEN")
            {
                var prev = ctx.ResolveNode(inSrc.uid, inSrc.port, depth);
                if (!string.IsNullOrEmpty(prev))
                {
                    if (prev.Contains(" OR ")) prev = $"({prev})";
                    if (expr.Contains(" OR ")) expr = $"({expr})";
                    return $"{prev} AND {expr}";
                }
            }
            return expr;
        }

        // ── Geração de linha para bobina ──────────────────────────────────────
        private static void CollectCoil(
            string uid, XElement part, string partName,
            FbdContext ctx, List<string> result)
        {
            // Operand: variável que recebe o valor
            string operand = "";
            if (ctx.WireFrom.TryGetValue((uid, "operand"), out var opSrc))
                operand = ctx.ResolveNode(opSrc.uid, opSrc.port, 0);
            if (string.IsNullOrEmpty(operand)) return;

            // Condição: valor booleano na entrada "in"
            string condition = "";
            if (ctx.WireFrom.TryGetValue((uid, "in"), out var inSrc) &&
                inSrc.uid != "PWR" && inSrc.uid != "OPEN")
                condition = ctx.ResolveNode(inSrc.uid, inSrc.port, 0);

            // Bit de memória de borda (PCoil / NCoil): porta "bit"
            string bitMem = "";
            if ((partName == "PCoil" || partName == "NCoil") &&
                ctx.WireFrom.TryGetValue((uid, "bit"), out var bitSrc))
                bitMem = $" [MEM:{ctx.ResolveNode(bitSrc.uid, bitSrc.port, 0)}]";

            bool always  = string.IsNullOrEmpty(condition);
            bool negated = part.Elements()
                .Any(e => e.Name.LocalName == "Negated" &&
                          e.Attribute("Name")?.Value == "operand");

            switch (partName)
            {
                case "SCoil":
                    result.Add(always ? $"SET   {operand}"
                                      : $"IF {condition} THEN SET {operand}");
                    break;
                case "RCoil":
                    result.Add(always ? $"RESET {operand}"
                                      : $"IF {condition} THEN RESET {operand}");
                    break;
                case "PCoil":
                    // P=: assigns 1 for ONE CYCLE on rising edge, 0 otherwise — NOT a sticky SET
                    result.Add(always
                        ? $"{operand} := ↑(TRUE){bitMem}"
                        : $"{operand} := ↑({condition}){bitMem}");
                    break;
                case "NCoil":
                    // N=: assigns 1 for ONE CYCLE on falling edge, 0 otherwise — NOT a sticky SET
                    result.Add(always
                        ? $"{operand} := ↓(TRUE){bitMem}"
                        : $"{operand} := ↓({condition}){bitMem}");
                    break;
                default: // Coil normal: = ou /=
                    result.Add(negated
                        ? (always ? $"{operand} := NOT(TRUE)" : $"{operand} := NOT({condition})")
                        : (always ? $"{operand} := TRUE"      : $"{operand} := {condition}"));
                    break;
            }
        }

        // ── SR flip-flop ──────────────────────────────────────────────────────
        private static void CollectSr(string uid, FbdContext ctx, List<string> result)
        {
            string operand = "?";
            if (ctx.WireFrom.TryGetValue((uid, "operand"), out var opSrc))
                operand = ctx.ResolveNode(opSrc.uid, opSrc.port, 0);

            // Portas minúsculas: s, r1
            string sIn  = "(sem ligação)";
            string r1In = "(sem ligação)";
            if (ctx.WireFrom.TryGetValue((uid, "s"),  out var sSrc))  sIn  = ctx.ResolveNode(sSrc.uid,  sSrc.port,  0);
            if (ctx.WireFrom.TryGetValue((uid, "r1"), out var r1Src)) r1In = ctx.ResolveNode(r1Src.uid, r1Src.port, 0);

            result.Add($"SR flip-flop  {operand}:  // R1 tem prioridade");
            result.Add($"  S  := {sIn}");
            result.Add($"  R1 := {r1In}");
        }

        // ── RS flip-flop ──────────────────────────────────────────────────────
        private static void CollectRs(string uid, FbdContext ctx, List<string> result)
        {
            string operand = "?";
            if (ctx.WireFrom.TryGetValue((uid, "operand"), out var opSrc))
                operand = ctx.ResolveNode(opSrc.uid, opSrc.port, 0);

            // Portas minúsculas: s1, r
            string s1In = "(sem ligação)";
            string rIn  = "(sem ligação)";
            if (ctx.WireFrom.TryGetValue((uid, "s1"), out var s1Src)) s1In = ctx.ResolveNode(s1Src.uid, s1Src.port, 0);
            if (ctx.WireFrom.TryGetValue((uid, "r"),  out var rSrc))  rIn  = ctx.ResolveNode(rSrc.uid,  rSrc.port,  0);

            result.Add($"RS flip-flop  {operand}:  // S1 tem prioridade");
            result.Add($"  S1 := {s1In}");
            result.Add($"  R  := {rIn}");
        }

        // ── R_TRIG / F_TRIG (Part com Instance) ──────────────────────────────
        // Saída Q vai direto para IdentCon via wireTo[(uid, "Q")] = [(accessUid, _)]
        private static void CollectRtrig(string uid, XElement part, string partName, FbdContext ctx, List<string> result)
        {
            var inst = FbdContext.GetPartInstanceName(part) ?? $"{partName}_inst";

            // CLK entrada
            string clk = "?";
            if (ctx.WireFrom.TryGetValue((uid, "CLK"), out var clkSrc))
                clk = ctx.ResolveNode(clkSrc.uid, clkSrc.port, 0);

            // EN condição (opcional — normalmente OpenCon)
            string enCond = "";
            if (ctx.WireFrom.TryGetValue((uid, "en"), out var enSrc) &&
                enSrc.uid != "PWR" && enSrc.uid != "OPEN")
                enCond = ctx.ResolveNode(enSrc.uid, enSrc.port, 0);

            // Saída Q → IdentCon (variável de destino)
            if (ctx.WireTo.TryGetValue((uid, "Q"), out var qDsts))
            {
                foreach (var dst in qDsts)
                {
                    // destino pode ser IdentCon (Access) ou NameCon (entrada de outra parte)
                    if (ctx.AccessMap.ContainsKey(dst.uid))
                    {
                        var varName = ctx.ResolveDestination(dst.uid, dst.port);
                        var prefix  = string.IsNullOrEmpty(enCond) ? "" : $"IF {enCond}: ";
                        var edge    = partName == "R_TRIG" ? "↑" : "↓";
                        result.Add($"{prefix}{varName} := {edge}{inst}(CLK:={clk})");
                    }
                }
            }

            // Caso Q não tenha destino explícito, registra a chamada mesmo assim
            if (!ctx.WireTo.ContainsKey((uid, "Q")))
            {
                var edge = partName == "R_TRIG" ? "R_TRIG" : "F_TRIG";
                var prefix = string.IsNullOrEmpty(enCond) ? "" : $"IF {enCond}: ";
                result.Add($"{prefix}{inst}(CLK:={clk})  // {edge}");
            }
        }
    }
}
