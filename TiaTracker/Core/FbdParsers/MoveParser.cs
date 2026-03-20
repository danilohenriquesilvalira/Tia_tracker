using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Move Operations (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Move operation").
    ///
    /// Instruções confirmadas por XML real:
    ///   Move             — MOVE          (in → out1..outN)   ← porta "out1", NÃO "out"!
    ///   MoveBlockI       — MOVE_BLK      (in, count → out)   ← portas MINÚSCULAS
    ///   MoveBlockU       — UMOVE_BLK     (in, count → out)   ← portas MINÚSCULAS
    ///   MOVE_BLK_VARIANT — MOVE_BLK_VARIANT (SRC, COUNT, SRC_INDEX, DEST_INDEX → Ret_Val, DEST)
    ///   Deserialize      — Deserialize   (SRC_ARRAY, POS → Ret_Val, DEST_VARIABLE)
    ///   Serialize        — Serialize     (SRC_VARIABLE, POS → Ret_Val, DEST_ARRAY)
    ///
    /// Instruções pendentes de verificação via XML real (mantidas como best-guess):
    ///   FillBlk  — FILL_BLK   (in, count → out)
    ///   UFillBlk — UFILL_BLK  (in, count → out)
    ///   Swap     — SWAP       (in → out)
    ///
    /// Estrutura XML (exemplo Move):
    ///   <Part Name="Move" UId="21">
    ///     <TemplateValue Name="Card" Type="Cardinality">1</TemplateValue>
    ///   </Part>
    ///   Wire: IdentCon(src) → NameCon(21,"in")
    ///   Wire: NameCon(21,"out1") → IdentCon(dst)   ← "out1", não "out"!
    ///
    /// Estrutura XML (MoveBlockI):
    ///   <Part Name="MoveBlockI" UId="24">
    ///     <TemplateValue Name="Card" Type="Cardinality">1</TemplateValue>
    ///   </Part>
    ///   Wire: NameCon(24,"out") → IdentCon(dst)    ← lowercase "out"
    ///
    /// Estrutura XML (MOVE_BLK_VARIANT):
    ///   <Part Name="MOVE_BLK_VARIANT" Version="1.2" UId="28">
    ///   Portas de entrada: SRC, COUNT, SRC_INDEX, DEST_INDEX  (MAIÚSCULAS)
    ///   Portas de saída:   Ret_Val → IdentCon,  DEST → IdentCon
    ///
    /// Estrutura XML (Deserialize / Serialize):
    ///   <Part Name="Deserialize" Version="2.2" UId="32">
    ///   Deserialize: SRC_ARRAY(in), POS(InOut) → Ret_Val(out), DEST_VARIABLE(out)
    ///   Serialize:   SRC_VARIABLE(in), POS(InOut) → Ret_Val(out), DEST_ARRAY(out)
    ///
    /// Saídas geradas por CollectOutputs:
    ///   Move             → "dst := src"                       (uma linha por outN)
    ///   MoveBlockI/U     → "MOVE_BLK(IN:=..., COUNT:=..., OUT:=dst)"
    ///   MOVE_BLK_VARIANT → "ret := MOVE_BLK_VARIANT(SRC:=..., ..., DEST:=dst)"
    ///   Deserialize      → "ret := Deserialize(SRC_ARRAY:=..., POS:=..., DEST_VARIABLE:=dst)"
    ///   Serialize        → "ret := Serialize(SRC_VARIABLE:=..., POS:=..., DEST_ARRAY:=dst)"
    /// </summary>
    internal static class MoveParser
    {
        internal static readonly HashSet<string> Handled = new HashSet<string>
        {
            // Confirmados por XML real
            "Move", "MoveBlockI", "MoveBlockU", "MOVE_BLK_VARIANT", "Deserialize", "Serialize",
            // Pendentes de verificação (best-guess mantido)
            "FillBlk", "UFillBlk", "Swap",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!Handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Move":             return $"MOVE({Inp("in")})";
                case "MoveBlockI":       return $"MOVE_BLK(IN:={Inp("in")}, COUNT:={Inp("count")})";
                case "MoveBlockU":       return $"UMOVE_BLK(IN:={Inp("in")}, COUNT:={Inp("count")})";
                case "MOVE_BLK_VARIANT": return $"MOVE_BLK_VARIANT(SRC:={Inp("SRC")}, COUNT:={Inp("COUNT")}, SRC_INDEX:={Inp("SRC_INDEX")}, DEST_INDEX:={Inp("DEST_INDEX")})";
                case "Deserialize":      return $"Deserialize(SRC_ARRAY:={Inp("SRC_ARRAY")}, POS:={Inp("POS")})";
                case "Serialize":        return $"Serialize(SRC_VARIABLE:={Inp("SRC_VARIABLE")}, POS:={Inp("POS")})";
                // Pendentes
                case "FillBlk":          return $"FILL_BLK(IN:={Inp("in")}, COUNT:={Inp("count")})";
                case "UFillBlk":         return $"UFILL_BLK(IN:={Inp("in")}, COUNT:={Inp("count")})";
                case "Swap":             return $"SWAP({Inp("in")})";
                default:                 return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!Handled.Contains(name)) continue;

                var neg = FbdContext.GetNegatedPorts(part);
                string Inp(string p) => ctx.Inp(uid, p, neg, 0);

                // Condição EN — se conectada a uma variável real (não PowerRail nem OpenCon)
                string enCond = "";
                if (ctx.WireFrom.TryGetValue((uid, "en"), out var enSrc) &&
                    enSrc.uid != "PWR" && enSrc.uid != "OPEN")
                    enCond = ctx.ResolveNode(enSrc.uid, enSrc.port, 0);
                string En(string line) => string.IsNullOrEmpty(enCond) ? line : $"IF {enCond} THEN {line}";

                switch (name)
                {
                    // ── Move: out1..outN → var := in ─────────────────────────────────
                    case "Move":
                    {
                        int card = FbdContext.GetCardinality(part, 1);
                        for (int i = 1; i <= card; i++)
                        {
                            var outPort = $"out{i}";
                            if (!ctx.WireTo.TryGetValue((uid, outPort), out var dsts)) continue;
                            foreach (var dst in dsts)
                            {
                                if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                                result.Add(En($"{ctx.ResolveDestination(dst.uid, dst.port)} := {Inp("in")}"));
                            }
                        }
                        break;
                    }

                    // ── MoveBlockI: MOVE_BLK(IN:=..., COUNT:=..., OUT:=dst) ──────────
                    case "MoveBlockI":
                    {
                        if (!ctx.WireTo.TryGetValue((uid, "out"), out var dsts)) break;
                        foreach (var dst in dsts)
                        {
                            if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                            var destVar = ctx.ResolveDestination(dst.uid, dst.port);
                            result.Add(En($"MOVE_BLK(IN:={Inp("in")}, COUNT:={Inp("count")}, OUT:={destVar})"));
                        }
                        break;
                    }

                    // ── MoveBlockU: UMOVE_BLK(IN:=..., COUNT:=..., OUT:=dst) ─────────
                    case "MoveBlockU":
                    {
                        if (!ctx.WireTo.TryGetValue((uid, "out"), out var dsts)) break;
                        foreach (var dst in dsts)
                        {
                            if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                            var destVar = ctx.ResolveDestination(dst.uid, dst.port);
                            result.Add(En($"UMOVE_BLK(IN:={Inp("in")}, COUNT:={Inp("count")}, OUT:={destVar})"));
                        }
                        break;
                    }

                    // ── MOVE_BLK_VARIANT: ret := MOVE_BLK_VARIANT(..., DEST:=dst) ────
                    case "MOVE_BLK_VARIANT":
                    {
                        string destParam = "?";
                        if (ctx.WireTo.TryGetValue((uid, "DEST"), out var destDsts))
                            foreach (var dst in destDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                { destParam = ctx.ResolveDestination(dst.uid, dst.port); break; }

                        var callExpr = $"MOVE_BLK_VARIANT(SRC:={Inp("SRC")}, COUNT:={Inp("COUNT")}, SRC_INDEX:={Inp("SRC_INDEX")}, DEST_INDEX:={Inp("DEST_INDEX")}, DEST:={destParam})";

                        if (ctx.WireTo.TryGetValue((uid, "Ret_Val"), out var retDsts))
                            foreach (var dst in retDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                    result.Add(En($"{ctx.ResolveDestination(dst.uid, dst.port)} := {callExpr}"));
                        break;
                    }

                    // ── Deserialize: ret := Deserialize(..., DEST_VARIABLE:=dst) ──────
                    case "Deserialize":
                    {
                        string destParam = "?";
                        if (ctx.WireTo.TryGetValue((uid, "DEST_VARIABLE"), out var destDsts))
                            foreach (var dst in destDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                { destParam = ctx.ResolveDestination(dst.uid, dst.port); break; }

                        var callExpr = $"Deserialize(SRC_ARRAY:={Inp("SRC_ARRAY")}, POS:={Inp("POS")}, DEST_VARIABLE:={destParam})";

                        if (ctx.WireTo.TryGetValue((uid, "Ret_Val"), out var retDsts))
                            foreach (var dst in retDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                    result.Add(En($"{ctx.ResolveDestination(dst.uid, dst.port)} := {callExpr}"));
                        break;
                    }

                    // ── Serialize: ret := Serialize(..., DEST_ARRAY:=dst) ────────────
                    case "Serialize":
                    {
                        string destParam = "?";
                        if (ctx.WireTo.TryGetValue((uid, "DEST_ARRAY"), out var destDsts))
                            foreach (var dst in destDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                { destParam = ctx.ResolveDestination(dst.uid, dst.port); break; }

                        var callExpr = $"Serialize(SRC_VARIABLE:={Inp("SRC_VARIABLE")}, POS:={Inp("POS")}, DEST_ARRAY:={destParam})";

                        if (ctx.WireTo.TryGetValue((uid, "Ret_Val"), out var retDsts))
                            foreach (var dst in retDsts)
                                if (ctx.AccessMap.ContainsKey(dst.uid))
                                    result.Add(En($"{ctx.ResolveDestination(dst.uid, dst.port)} := {callExpr}"));
                        break;
                    }

                    // ── Pendentes (best-guess) ────────────────────────────────────────
                    case "FillBlk":
                    case "UFillBlk":
                    {
                        if (!ctx.WireTo.TryGetValue((uid, "out"), out var dsts)) break;
                        var scl = name == "FillBlk" ? "FILL_BLK" : "UFILL_BLK";
                        foreach (var dst in dsts)
                        {
                            if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                            var destVar = ctx.ResolveDestination(dst.uid, dst.port);
                            result.Add(En($"{scl}(IN:={Inp("in")}, COUNT:={Inp("count")}, OUT:={destVar})"));
                        }
                        break;
                    }

                    case "Swap":
                    {
                        if (!ctx.WireTo.TryGetValue((uid, "out"), out var dsts)) break;
                        foreach (var dst in dsts)
                        {
                            if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                            result.Add(En($"{ctx.ResolveDestination(dst.uid, dst.port)} := SWAP({Inp("in")})"));
                        }
                        break;
                    }
                }
            }
        }
    }
}
