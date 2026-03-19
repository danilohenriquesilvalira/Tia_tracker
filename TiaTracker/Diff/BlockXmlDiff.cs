using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using TiaTracker.Models;

namespace TiaTracker.Diff
{
    /// <summary>
    /// Compara dois XML de blocos TIA Portal e detecta alterações por Network.
    /// Suporta LAD, FBD, SCL, STL.
    /// </summary>
    public static class BlockXmlDiff
    {
        public static List<NetworkChange> Compare(string xmlOld, string xmlNew)
        {
            var changes = new List<NetworkChange>();

            XDocument docOld, docNew;
            try
            {
                docOld = XDocument.Parse(xmlOld);
                docNew = XDocument.Parse(xmlNew);
            }
            catch { return changes; }

            var networksOld = GetNetworks(docOld);
            var networksNew = GetNetworks(docNew);

            int maxCount = Math.Max(networksOld.Count, networksNew.Count);

            for (int i = 0; i < maxCount; i++)
            {
                if (i >= networksNew.Count)
                {
                    changes.Add(new NetworkChange
                    {
                        Type     = ChangeType.Removed,
                        Index    = i + 1,
                        Title    = GetNetworkTitle(networksOld[i]),
                        Language = GetLanguage(networksOld[i]),
                        Changes  = new List<string> { "Network removida" }
                    });
                }
                else if (i >= networksOld.Count)
                {
                    changes.Add(new NetworkChange
                    {
                        Type     = ChangeType.Added,
                        Index    = i + 1,
                        Title    = GetNetworkTitle(networksNew[i]),
                        Language = GetLanguage(networksNew[i]),
                        Changes  = new List<string> { "Network adicionada" }
                    });
                }
                else
                {
                    var lang    = GetLanguage(networksNew[i]);
                    var details = CompareNetwork(networksOld[i], networksNew[i], lang);
                    if (details.Count > 0)
                    {
                        changes.Add(new NetworkChange
                        {
                            Type     = ChangeType.Modified,
                            Index    = i + 1,
                            Title    = GetNetworkTitle(networksNew[i]),
                            Language = lang,
                            Changes  = details
                        });
                    }
                }
            }

            return changes;
        }

        // ── Extrai networks (CompileUnit) do XML ──────────────────────────────

        private static List<XElement> GetNetworks(XDocument doc)
        {
            return doc.Descendants()
                .Where(e => e.Name.LocalName == "CompileUnit")
                .ToList();
        }

        private static string GetNetworkTitle(XElement network)
        {
            // Título está em MultilingualText CompositionName="Title"
            var titleEl = network.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "MultilingualText" &&
                    (string)e.Attribute("CompositionName") == "Title");

            if (titleEl == null) return "";

            var textEl = titleEl.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Text");

            return textEl?.Value?.Trim() ?? "";
        }

        private static string GetLanguage(XElement network)
        {
            var langEl = network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ProgrammingLanguage");
            return langEl?.Value ?? "LAD";
        }

        // ── Compara o conteúdo de uma network ─────────────────────────────────

        private static List<string> CompareNetwork(XElement oldNet, XElement newNet, string lang)
        {
            if (lang == "SCL" || lang == "STL")
                return CompareTextNetwork(oldNet, newNet, lang);
            else
                return CompareGraphicNetwork(oldNet, newNet);
        }

        // SCL / STL: comparar linha a linha
        private static List<string> CompareTextNetwork(XElement oldNet, XElement newNet, string lang)
        {
            var changes = new List<string>();

            var oldBody = GetTextBody(oldNet);
            var newBody = GetTextBody(newNet);

            if (oldBody == newBody) return changes;

            var oldLines = oldBody.Split('\n');
            var newLines = newBody.Split('\n');

            // Linhas removidas
            var removed = oldLines.Except(newLines)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            // Linhas adicionadas
            var added = newLines.Except(oldLines)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            foreach (var l in removed) changes.Add($"  - {l}");
            foreach (var l in added)   changes.Add($"  + {l}");

            return changes;
        }

        private static string GetTextBody(XElement network)
        {
            var body = network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Body");
            return body?.Value?.Trim() ?? "";
        }

        // LAD / FBD: comparar elementos XML (instruções, bobinas, contacts, etc.)
        private static List<string> CompareGraphicNetwork(XElement oldNet, XElement newNet)
        {
            var changes = new List<string>();

            var oldSource = GetNetworkSource(oldNet);
            var newSource = GetNetworkSource(newNet);

            if (oldSource == null && newSource == null) return changes;

            var oldXml = oldSource?.ToString() ?? "";
            var newXml = newSource?.ToString() ?? "";

            if (oldXml == newXml) return changes;

            // Extrair instruções/elementos relevantes de cada versão
            var oldElems = ExtractElements(oldSource);
            var newElems = ExtractElements(newSource);

            var removed = oldElems.Except(newElems).ToList();
            var added   = newElems.Except(oldElems).ToList();

            foreach (var e in removed) changes.Add($"  - {e}");
            foreach (var e in added)   changes.Add($"  + {e}");

            if (changes.Count == 0 && oldXml != newXml)
                changes.Add("  Alteração detectada (parâmetros internos)");

            return changes;
        }

        private static XElement GetNetworkSource(XElement network)
        {
            return network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
        }

        // Extrai uma lista de descrições legíveis dos elementos da network
        private static List<string> ExtractElements(XElement source)
        {
            if (source == null) return new List<string>();

            var result = new List<string>();

            // Contacts (--| |-- e --|/|--)
            foreach (var el in source.Descendants().Where(e => e.Name.LocalName == "Contact"))
            {
                var name    = GetComponentName(el);
                var negated = el.Attribute("Negated")?.Value == "true";
                result.Add($"Contact: {name}{(negated ? " [NF]" : " [NA]")}");
            }

            // Coils (-( )-)
            foreach (var el in source.Descendants().Where(e => e.Name.LocalName == "Coil"))
            {
                var name    = GetComponentName(el);
                var negated = el.Attribute("Negated")?.Value == "true";
                result.Add($"Coil: {name}{(negated ? " [Reset]" : "")}");
            }

            // Function calls (Call FC/FB)
            foreach (var el in source.Descendants().Where(e => e.Name.LocalName == "Call"))
            {
                var callInfo = el.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                var name = callInfo?.Attribute("Name")?.Value ?? "?";
                result.Add($"Call: {name}");
            }

            // Instructions (ADD, SUB, MOV, CMP, etc.)
            foreach (var el in source.Descendants().Where(e => e.Name.LocalName == "Part"))
            {
                var name = el.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name))
                    result.Add($"Inst: {name}");
            }

            // Access (variáveis/operandos usados)
            foreach (var el in source.Descendants().Where(e => e.Name.LocalName == "Access"))
            {
                var scope = el.Attribute("Scope")?.Value;
                if (scope == "GlobalVariable" || scope == "LocalVariable")
                {
                    var sym = el.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "Symbol");
                    var varName = string.Join(".", sym?.Elements()
                        .Select(c => c.Value) ?? new string[0]);
                    if (!string.IsNullOrEmpty(varName))
                        result.Add($"Var: {varName}");
                }
            }

            return result;
        }

        private static string GetComponentName(XElement el)
        {
            var access = el.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Access");
            if (access == null) return "?";

            var sym = access.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Symbol");
            return string.Join(".", sym?.Elements().Select(c => c.Value) ?? new string[0]);
        }
    }
}
