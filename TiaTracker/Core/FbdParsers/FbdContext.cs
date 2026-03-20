using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace TiaTracker.Core.FbdParsers
{
    /// <summary>
    /// Estado compartilhado de uma única rede FBD/LAD.
    /// Contém os mapas de wire e os helpers de resolução usados por todos os sub-parsers.
    /// </summary>
    internal sealed class FbdContext
    {
        // ── Mapas de elementos ────────────────────────────────────────────────
        internal readonly Dictionary<string, XElement> AccessMap;
        internal readonly Dictionary<string, XElement> PartMap;
        internal readonly Dictionary<string, XElement> CallMap;

        // wireFrom[(dstUid, dstPort)] = (srcUid, srcPort)  — resolução backward
        internal readonly Dictionary<(string uid, string port), (string uid, string port)> WireFrom;
        // wireTo[(srcUid, srcPort)]   = lista de (dstUid, dstPort)  — descoberta forward
        internal readonly Dictionary<(string uid, string port), List<(string uid, string port)>> WireTo;

        internal FbdContext(
            Dictionary<string, XElement> accessMap,
            Dictionary<string, XElement> partMap,
            Dictionary<string, XElement> callMap,
            Dictionary<(string uid, string port), (string uid, string port)> wireFrom,
            Dictionary<(string uid, string port), List<(string uid, string port)>> wireTo)
        {
            AccessMap = accessMap;
            PartMap   = partMap;
            CallMap   = callMap;
            WireFrom  = wireFrom;
            WireTo    = wireTo;
        }

        // ── Resolução principal ───────────────────────────────────────────────
        /// <summary>Resolve um nó de origem de wire em expressão legível.</summary>
        internal string ResolveNode(string uid, string port, int depth)
        {
            if (depth > 12) return "...";
            if (uid == "PWR")  return "(PowerRail)";
            if (uid == "OPEN") return "(sem entrada)";

            // Acesso (variável ou constante)
            if (AccessMap.TryGetValue(uid, out var acc))
            {
                var scope = acc.Attribute("Scope")?.Value;
                if (scope == "LiteralConstant" || scope == "TypedConstant")
                    return acc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "?";
                return GetVarName(acc);
            }

            // Part (porta lógica, bloco funcional, etc.)
            if (PartMap.TryGetValue(uid, out var part))
                return Dispatch(uid, part, depth + 1);

            // Call (chamada de FC/FB)
            if (CallMap.TryGetValue(uid, out var call))
            {
                var ci   = call.Descendants().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                var name = ci?.Attribute("Name")?.Value ?? "?";
                var inst = GetCallInstanceName(call);
                return inst != null ? $"{inst}.{port}" : $"{name}.{port}";
            }

            return $"#{uid}.{port}";
        }

        /// <summary>Despacha resolução de Part para o parser correto.</summary>
        private string Dispatch(string uid, XElement part, int depth)
        {
            return BitLogicParser.Resolve(uid, part, this, depth)
                ?? TimerParser.Resolve(uid, part, this, depth)
                ?? CounterParser.Resolve(uid, part, this, depth)
                ?? ComparatorParser.Resolve(uid, part, this, depth)
                ?? MathParser.Resolve(uid, part, this, depth)
                ?? MoveParser.Resolve(uid, part, this, depth)
                ?? ConversionParser.Resolve(uid, part, this, depth)
                ?? WordLogicParser.Resolve(uid, part, this, depth)
                ?? StringParser.Resolve(uid, part, this, depth)
                ?? ProgramControlParser.Resolve(uid, part, this, depth)
                ?? GenericFallback(uid, part, depth);
        }

        /// <summary>Fallback genérico para Parts não reconhecidos — coleta todas as entradas conectadas.</summary>
        private string GenericFallback(string uid, XElement part, int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "?";
            var neg  = GetNegatedPorts(part);

            var connectedPorts = WireFrom.Keys
                .Where(k => k.uid == uid)
                .OrderBy(k => k.port)
                .ToList();

            if (connectedPorts.Count == 0)
                return name;

            var sb = new StringBuilder();
            sb.Append(name).Append('(');
            bool first = true;
            foreach (var pk in connectedPorts)
            {
                if (!first) sb.Append(", ");
                var src = WireFrom[pk];
                var v   = ResolveNode(src.uid, src.port, depth);
                if (neg.Contains(pk.port)) v = $"NOT({v})";
                sb.Append($"{pk.port}:={v}");
                first = false;
            }
            sb.Append(')');
            return sb.ToString();
        }

        // ── Helpers compartilhados ────────────────────────────────────────────

        /// <summary>Resolve uma porta de entrada, aplicando negação se necessário.</summary>
        internal string Inp(string uid, string portName, HashSet<string> negated, int depth)
        {
            if (!WireFrom.TryGetValue((uid, portName), out var s)) return "?";
            var v = ResolveNode(s.uid, s.port, depth);
            return negated.Contains(portName) ? $"NOT({v})" : v;
        }

        /// <summary>Coleta todas as entradas numeradas in1, in2, ..., in{card}.</summary>
        internal List<string> CollectInputs(string uid, int card, HashSet<string> negated, int depth)
        {
            var result = new List<string>();
            for (int i = 1; i <= card; i++)
            {
                var portName = $"in{i}";
                if (!WireFrom.TryGetValue((uid, portName), out var s)) continue;
                var v = ResolveNode(s.uid, s.port, depth);
                result.Add(negated.Contains(portName) ? $"NOT({v})" : v);
            }
            return result;
        }

        /// <summary>Nome legível de um elemento Access (variável ou constante).</summary>
        internal string GetVarName(XElement acc)
        {
            var scope = acc.Attribute("Scope")?.Value;

            // Endereço absoluto Siemens: DB40.DBW0 / I0.0 / MW10 / QB0 etc.
            if (scope == "Address")
            {
                var addr = acc.Elements().FirstOrDefault(e => e.Name.LocalName == "Address");
                if (addr != null)
                {
                    var area    = addr.Attribute("Area")?.Value ?? "?";
                    var type    = addr.Attribute("Type")?.Value ?? "";
                    var block   = addr.Attribute("BlockNumber")?.Value ?? "";
                    int bitOff  = int.TryParse(addr.Attribute("BitOffset")?.Value, out int b) ? b : 0;
                    int byteOff = bitOff / 8;
                    int bitIdx  = bitOff % 8;

                    if (area == "DB")
                        return type switch
                        {
                            "Bool"  => $"DB{block}.DBX{byteOff}.{bitIdx}",
                            "Byte"  => $"DB{block}.DBB{byteOff}",
                            "Word"  => $"DB{block}.DBW{byteOff}",
                            "DWord" => $"DB{block}.DBD{byteOff}",
                            _       => $"DB{block}.{byteOff}"
                        };
                    else
                        return type switch
                        {
                            "Bool"  => $"{area}{byteOff}.{bitIdx}",
                            "Byte"  => $"{area}B{byteOff}",
                            "Word"  => $"{area}W{byteOff}",
                            "DWord" => $"{area}D{byteOff}",
                            _       => $"{area}{byteOff}"
                        };
                }
            }

            var sym = acc.Elements().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym != null) return GetVarNameFromSymbol(sym);

            // Indexação de array
            var comp = acc.Elements().FirstOrDefault(e => e.Name.LocalName == "Component");
            if (comp != null)
            {
                var nm = comp.Attribute("Name")?.Value ?? "?";
                if (comp.Attribute("AccessModifier")?.Value == "Array")
                {
                    var indices = comp.Elements()
                        .Where(e => e.Name.LocalName == "Access")
                        .Select(e => ResolveNode(e.Attribute("UId")?.Value ?? "", "out", 0))
                        .ToList();
                    return $"{nm}[{string.Join(",", indices)}]";
                }
                return nm;
            }

            var cv = acc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue");
            return cv?.Value ?? "?";
        }

        internal static string GetVarNameFromSymbol(XElement sym)
        {
            var sb = new StringBuilder();
            bool lastWasComponent = false;
            foreach (var el in sym.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "Component":
                        var compName  = el.Attribute("Name")?.Value ?? "";
                        var hasQuotes = el.Elements().Any(e =>
                            e.Name.LocalName == "BooleanAttribute" &&
                            e.Attribute("Name")?.Value == "HasQuotes" &&
                            e.Value == "true");
                        if (lastWasComponent && sb.Length > 0) sb.Append('.');
                        sb.Append(hasQuotes ? $"\"{compName}\"" : compName);
                        // Handle array index: <Component AccessModifier="Array"><Access ...>
                        if (el.Attribute("AccessModifier")?.Value == "Array")
                        {
                            var idxParts = el.Elements()
                                .Where(e => e.Name.LocalName == "Access")
                                .Select(e => {
                                    var s = e.Attribute("Scope")?.Value;
                                    if (s == "LiteralConstant" || s == "TypedConstant")
                                        return e.Descendants()
                                            .FirstOrDefault(x => x.Name.LocalName == "ConstantValue")
                                            ?.Value ?? "?";
                                    var innerSym = e.Elements()
                                        .FirstOrDefault(x => x.Name.LocalName == "Symbol");
                                    return innerSym != null ? GetVarNameFromSymbol(innerSym) : "?";
                                })
                                .ToList();
                            if (idxParts.Count > 0)
                                sb.Append($"[{string.Join(",", idxParts)}]");
                        }
                        lastWasComponent = true;
                        break;
                    case "Token":
                        sb.Append(el.Attribute("Text")?.Value ?? "");
                        lastWasComponent = false;
                        break;
                    default:
                        lastWasComponent = false;
                        break;
                }
            }
            return sb.Length > 0 ? sb.ToString() : "?";
        }

        /// <summary>Nome da instância de timer/counter/FB a partir do elemento Part.</summary>
        internal static string GetPartInstanceName(XElement part)
        {
            var instEl = part.Elements().FirstOrDefault(e => e.Name.LocalName == "Instance");
            if (instEl == null) return null;
            var sym = instEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym != null) return GetVarNameFromSymbol(sym);
            var comp = instEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Component");
            return comp?.Attribute("Name")?.Value;
        }

        /// <summary>Nome da instância de uma chamada de FC/FB.</summary>
        internal static string GetCallInstanceName(XElement call)
        {
            var instEl = call.Descendants().FirstOrDefault(e => e.Name.LocalName == "Instance");
            if (instEl == null) return null;
            var sym = instEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym != null) return GetVarNameFromSymbol(sym);
            var comp = instEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Component");
            return comp?.Attribute("Name")?.Value;
        }

        /// <summary>Nome legível do destino de um wire (para logs de saída).</summary>
        internal string ResolveDestination(string uid, string port)
        {
            if (AccessMap.TryGetValue(uid, out var acc)) return GetVarName(acc);
            if (PartMap.TryGetValue(uid, out var part))  return $"[{part.Attribute("Name")?.Value}].{port}";
            return $"#{uid}.{port}";
        }

        // ── Helpers de leitura de Part ────────────────────────────────────────

        /// <summary>Conjunto de portas negadas via &lt;Negated Name="..."/&gt;.</summary>
        internal static HashSet<string> GetNegatedPorts(XElement part)
            => new HashSet<string>(
                part.Elements().Where(e => e.Name.LocalName == "Negated")
                    .Select(e => e.Attribute("Name")?.Value ?? "")
                    .Where(s => s.Length > 0));

        /// <summary>Cardinalidade de portas (TemplateValue Name="Card").</summary>
        internal static int GetCardinality(XElement part, int defaultCard = 2)
        {
            var el = part.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "TemplateValue" && e.Attribute("Name")?.Value == "Card");
            if (el != null && int.TryParse(el.Value, out int c)) return c;
            return defaultCard;
        }

        /// <summary>Valor de um TemplateValue pelo nome.</summary>
        internal static string GetTemplateValue(XElement part, string templateName)
            => part.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "TemplateValue" &&
                    e.Attribute("Name")?.Value == templateName)?.Value;
    }
}
