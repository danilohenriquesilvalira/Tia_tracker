using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Move Operations (FBD/LAD) — S7-1200/S7-1500.
    /// TODO: verificar portas exatas via XML real.
    ///
    /// Instruções a suportar:
    ///   Move     — Mover valor (en, in → eno, out1, out2, ...)
    ///   MoveBlk  — Mover bloco (en, IN, COUNT → eno, OUT)
    ///   UMoveBlk — Mover bloco não interrompível
    ///   FillBlk  — Preencher bloco (en, IN, COUNT → eno, OUT)
    ///   UFillBlk — Preencher não interrompível
    ///   Swap     — Trocar bytes (en, in → eno, out)
    /// </summary>
    internal static class MoveParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "Move", "MoveBlk", "UMoveBlk", "Fill", "FillBlk", "UFillBlk", "Swap",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var neg = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "Move":     return $"MOVE({Inp("in")})";
                case "Swap":     return $"SWAP({Inp("in")})";
                case "Fill":     return $"FILL(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";
                case "MoveBlk":  return $"MOVE_BLK(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";
                case "UMoveBlk": return $"UMOVE_BLK(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";
                case "FillBlk":  return $"FILL_BLK(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";
                case "UFillBlk": return $"UFILL_BLK(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";
                default:         return null;
            }
        }

        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                var neg = FbdContext.GetNegatedPorts(part);
                string Inp(string p) => ctx.Inp(uid, p, neg, 0);

                // EN condition
                string enCond = "";
                if (ctx.WireFrom.TryGetValue((uid, "en"), out var enSrc) &&
                    enSrc.uid != "PWR" && enSrc.uid != "OPEN")
                    enCond = ctx.ResolveNode(enSrc.uid, enSrc.port, 0);

                string prefix = string.IsNullOrEmpty(enCond) ? "" : $"IF {enCond}: ";

                // Collect out1, out2, ... destinations
                int card = FbdContext.GetCardinality(part, 1);
                for (int i = 1; i <= card; i++)
                {
                    var outPort = $"out{i}";
                    if (!ctx.WireTo.TryGetValue((uid, outPort), out var dsts)) continue;
                    foreach (var dst in dsts)
                    {
                        if (!ctx.AccessMap.ContainsKey(dst.uid)) continue;
                        var varName = ctx.ResolveDestination(dst.uid, dst.port);
                        result.Add($"{prefix}{varName} := {Inp("in")}  // MOVE");
                    }
                }

                // OUT (MoveBlk/FillBlk)
                if (ctx.WireTo.TryGetValue((uid, "OUT"), out var outDsts))
                    foreach (var dst in outDsts)
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                            result.Add($"{prefix}{ctx.ResolveDestination(dst.uid, dst.port)} := {Inp("IN")}  // {name}");
            }
        }
    }
}
