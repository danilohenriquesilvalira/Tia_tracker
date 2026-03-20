using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Parser de Counter Operations (FBD/LAD) — S7-1200/S7-1500.
    /// Verificado com XML real exportado do TIA Portal V18 (FC "Counter operation").
    ///
    /// Instruções suportadas:
    ///   CTU  — Count up        (CU, R, PV  →  Q → Coil,  CV → IdentCon)
    ///   CTD  — Count down      (CD, LD, PV →  Q → Coil,  CV → IdentCon)
    ///   CTUD — Count up/down   (CU, CD, R, LD, PV  →  QU → Coil,  QD → IdentCon,  CV → IdentCon)
    ///
    /// Estrutura XML do Part (exemplo CTU):
    ///   <Part Name="CTU" Version="1.0" UId="26">
    ///     <Instance Scope="GlobalVariable" UId="27">
    ///       <Component Name="IEC_Counter_0_DB"/>
    ///     </Instance>
    ///     <TemplateValue Name="value_type" Type="Type">Int</TemplateValue>
    ///   </Part>
    ///
    /// Portas confirmadas (MAIÚSCULAS):
    ///   CTU : entradas CU, R, PV  — saída Q (→ Coil), CV (→ IdentCon direto)
    ///   CTD : entradas CD, LD, PV — saída Q (→ Coil), CV (→ IdentCon direto)
    ///   CTUD: entradas CU, CD, R, LD, PV
    ///         saídas QU (→ Coil), QD (→ IdentCon direto), CV (→ IdentCon direto)
    ///
    /// Fluxo de processamento:
    ///   Q / QU → Coil: BitLogicParser.CollectCoil resolve Coil.in → ResolveNode(counterUid)
    ///                  → Dispatch → CounterParser.Resolve() retorna expressão de chamada inline.
    ///   CV / QD → variável: CounterParser.CollectOutputs gera "var := inst.CV / inst.QD".
    /// </summary>
    internal static class CounterParser
    {
        private static readonly HashSet<string> _handled = new HashSet<string>
        {
            "CTU", "CTD", "CTUD",
        };

        internal static string Resolve(string uid, XElement part, FbdContext ctx, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "";
            if (!_handled.Contains(name)) return null;

            var inst = FbdContext.GetPartInstanceName(part) ?? name;
            var neg  = FbdContext.GetNegatedPorts(part);
            string Inp(string p) => ctx.Inp(uid, p, neg, depth);

            switch (name)
            {
                case "CTU":  return $"{inst}(CU:={Inp("CU")}, R:={Inp("R")}, PV:={Inp("PV")})";
                case "CTD":  return $"{inst}(CD:={Inp("CD")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                case "CTUD": return $"{inst}(CU:={Inp("CU")}, CD:={Inp("CD")}, R:={Inp("R")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                default:     return null;
            }
        }

        /// <summary>
        /// Coleta saídas diretas dos counters para variáveis (sem passar por Coil).
        ///   CTU / CTD : CV → variável
        ///   CTUD      : CV → variável  +  QD → variável
        /// Q e QU → Coil já são tratados por BitLogicParser.CollectOutputs.
        /// </summary>
        internal static void CollectOutputs(FbdContext ctx, List<string> result)
        {
            foreach (var kv in ctx.PartMap)
            {
                var uid  = kv.Key;
                var part = kv.Value;
                var name = part.Attribute("Name")?.Value ?? "";
                if (!_handled.Contains(name)) continue;

                var inst = FbdContext.GetPartInstanceName(part) ?? name;

                // CV → variável  (CTU, CTD, CTUD)
                if (ctx.WireTo.TryGetValue((uid, "CV"), out var cvDsts))
                    foreach (var dst in cvDsts)
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                            result.Add($"{ctx.ResolveDestination(dst.uid, dst.port)} := {inst}.CV");

                // QD → variável  (CTUD apenas — QU vai para Coil, QD vai direto para IdentCon)
                if (name == "CTUD" && ctx.WireTo.TryGetValue((uid, "QD"), out var qdDsts))
                    foreach (var dst in qdDsts)
                        if (ctx.AccessMap.ContainsKey(dst.uid))
                            result.Add($"{ctx.ResolveDestination(dst.uid, dst.port)} := {inst}.QD");
            }
        }
    }
}
