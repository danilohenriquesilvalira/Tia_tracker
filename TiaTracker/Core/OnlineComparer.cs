using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.Online;
using Siemens.Engineering.Compare;

namespace TiaTracker.Core
{
    public class CompareItem
    {
        public string             Name            { get; set; }
        public string             LeftName        { get; set; }
        public string             RightName       { get; set; }
        public CompareResultState State           { get; set; }
        public string             Detail          { get; set; }
        public List<string>       OfflineNetworks { get; set; } = new List<string>();
        public List<string>       OfflineTags     { get; set; } = new List<string>();
        public List<CompareItem>  Children        { get; set; } = new List<CompareItem>();
    }

    public class OnlineComparer
    {
        private readonly Project _project;

        public OnlineComparer(Project project)
        {
            _project = project;
        }

        public List<CompareItem> CompareAll()
        {
            var results = new List<CompareItem>();

            foreach (Device device in _project.Devices)
            {
                var result = CompareDevice(device);
                if (result != null) results.Add(result);
            }

            foreach (DeviceGroup group in _project.DeviceGroups)
                foreach (Device device in group.Devices)
                {
                    var result = CompareDevice(device);
                    if (result != null) results.Add(result);
                }

            return results;
        }

        private CompareItem CompareDevice(Device device)
        {
            DeviceItem plcItem = null;
            PlcSoftware plcSw  = null;

            foreach (DeviceItem item in device.DeviceItems)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc)
                {
                    plcItem = item;
                    plcSw   = plc;
                    break;
                }
            }

            if (plcSw == null) return null;

            Console.Write($"  A ligar online a {device.Name}...");
            var provider = plcItem.GetService<OnlineProvider>();
            if (provider == null)
            {
                Console.WriteLine(" sem OnlineProvider.");
                return null;
            }

            OnlineState state;
            try
            {
                state = provider.GoOnline();
                Console.WriteLine($" {state}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" ERRO GoOnline: {ex.Message}");
                return null;
            }

            if (state != OnlineState.Online)
            {
                Console.WriteLine($"  Nao foi possivel ligar: {state}");
                provider.GoOffline();
                return null;
            }

            CompareItem root = null;
            try
            {
                Console.Write($"  A comparar projecto vs PLC...");
                var compareResult = plcSw.CompareToOnline();
                Console.WriteLine(" OK");

                root = new CompareItem
                {
                    Name      = device.Name,
                    LeftName  = "Projecto (Offline)",
                    RightName = "PLC (Online)",
                    State     = compareResult.RootElement.ComparisonResult
                };

                WalkTree(compareResult.RootElement, root);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" ERRO Compare: {ex.Message}");
            }
            finally
            {
                // Ir offline ANTES de qualquer export
                provider.GoOffline();
            }

            // Enriquecer com conteúdo offline DEPOIS de desligar (read-only, seguro)
            if (root != null)
            {
                Console.Write("  A ler conteúdo offline dos blocos...");
                EnrichWithOfflineContent(root.Children, plcSw);
                Console.WriteLine(" OK");
            }

            return root;
        }

        private void WalkTree(CompareResultElement element, CompareItem parent)
        {
            foreach (CompareResultElement child in element.Elements)
            {
                if (child.ComparisonResult == CompareResultState.ObjectsIdentical ||
                    child.ComparisonResult == CompareResultState.FolderContentsIdentical ||
                    child.ComparisonResult == CompareResultState.CompareIrrelevant)
                    continue;

                var item = new CompareItem
                {
                    Name      = child.LeftName ?? child.RightName,
                    LeftName  = child.LeftName,
                    RightName = child.RightName,
                    State     = child.ComparisonResult,
                    Detail    = child.DetailedInformation
                };

                parent.Children.Add(item);
                WalkTree(child, item);
            }
        }

        // ── Enriquece com conteúdo offline (apenas leitura, sem modificar nada) ─

        private void EnrichWithOfflineContent(List<CompareItem> items, PlcSoftware plcSw)
        {
            foreach (var item in items)
            {
                if (item.State == CompareResultState.ObjectsDifferent ||
                    item.State == CompareResultState.RightMissing)
                {
                    var blockName = ExtractBracketName(item.LeftName ?? item.Name);

                    var block = FindBlock(plcSw.BlockGroup, blockName);
                    if (block != null)
                        item.OfflineNetworks = GetOfflineNetworks(block);
                    else
                    {
                        var table = FindTagTable(plcSw.TagTableGroup, blockName);
                        if (table != null)
                            item.OfflineTags = GetOfflineTags(table);
                    }
                }

                if (item.Children.Count > 0)
                    EnrichWithOfflineContent(item.Children, plcSw);
            }
        }

        private static string ExtractBracketName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var start = name.IndexOf('[');
            var end   = name.IndexOf(']');
            if (start >= 0 && end > start)
                return name.Substring(start + 1, end - start - 1).Trim();
            return name.Trim();
        }

        private static PlcBlock FindBlock(PlcBlockGroup group, string name)
        {
            var block = FindBlockExact(group, name);
            if (block != null) return block;

            var nameNoDigits = name.TrimEnd('0','1','2','3','4','5','6','7','8','9');
            if (nameNoDigits.Length > 0 && nameNoDigits != name)
            {
                block = FindBlockExact(group, nameNoDigits);
                if (block != null) return block;
            }
            return null;
        }

        private static PlcBlock FindBlockExact(PlcBlockGroup group, string name)
        {
            foreach (PlcBlock b in group.Blocks)
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            foreach (PlcBlockGroup sub in group.Groups)
            {
                var found = FindBlockExact(sub, name);
                if (found != null) return found;
            }
            return null;
        }

        private static PlcTagTable FindTagTable(PlcTagTableGroup group, string name)
        {
            foreach (PlcTagTable t in group.TagTables)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            foreach (PlcTagTableGroup sub in group.Groups)
            {
                var found = FindTagTable(sub, name);
                if (found != null) return found;
            }
            return null;
        }

        // ── Export offline (só leitura, nunca modifica o projecto) ────────────

        private static List<string> GetOfflineNetworks(PlcBlock block)
        {
            var result   = new List<string>();
            var tempFile = Path.Combine(Path.GetTempPath(),
                BlockExporter.Sanitize(block.Name) + "_cmp.xml");
            try
            {
                block.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                var xml = File.ReadAllText(tempFile);
                var doc = XDocument.Parse(xml);

                var networks = doc.Descendants()
                    .Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit")
                    .ToList();

                for (int i = 0; i < networks.Count; i++)
                {
                    var net   = networks[i];
                    var title = GetNetworkTitle(net);
                    var lang  = GetNetworkLang(net);
                    var label = string.IsNullOrEmpty(title)
                        ? $"Network {i + 1} [{lang}]"
                        : $"Network {i + 1} — {title} [{lang}]";

                    result.Add(label);

                    if (lang == "SCL" || lang == "STL")
                    {
                        var stEl = net.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "StructuredText");
                        string body;
                        if (stEl != null)
                            body = ReconstructScl(stEl);
                        else
                            body = net.Descendants()
                                .FirstOrDefault(e => e.Name.LocalName == "Body")?.Value?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(body))
                        {
                            var lines = body.Split('\n')
                                .Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
                            foreach (var line in lines.Take(10))
                                result.Add($"    {line}");
                            if (lines.Count > 10) result.Add($"    ... (+{lines.Count - 10} linhas)");
                        }
                    }
                    else
                    {
                        foreach (var instr in ExtractInstructions(net))
                            result.Add($"    {instr}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Add($"(erro ao ler offline: {ex.Message})");
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
            return result;
        }

        private static List<string> GetOfflineTags(PlcTagTable table)
        {
            var result   = new List<string>();
            var tempFile = Path.Combine(Path.GetTempPath(),
                BlockExporter.Sanitize(table.Name) + "_cmp.xml");
            try
            {
                table.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                var xml = File.ReadAllText(tempFile);
                var doc = XDocument.Parse(xml);

                var tags = doc.Descendants()
                    .Where(e => e.Name.LocalName == "SW.Tags.PlcTag")
                    .ToList();

                result.Add($"{tags.Count} tags:");
                foreach (var tag in tags.Take(20))
                {
                    var tagName  = tag.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "?";
                    var dataType = tag.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "DataTypeName")?.Value ?? "?";
                    var address  = tag.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "LogicalAddress")?.Value ?? "";
                    result.Add($"  {tagName} : {dataType}{(string.IsNullOrEmpty(address) ? "" : "  @ " + address)}");
                }
                if (tags.Count > 20)
                    result.Add($"  ... (+{tags.Count - 20} tags)");
            }
            catch (Exception ex)
            {
                result.Add($"(erro ao ler tags: {ex.Message})");
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
            return result;
        }

        private static string GetNetworkTitle(XElement network)
        {
            var titleEl = network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "MultilingualText" &&
                    (string)e.Attribute("CompositionName") == "Title");
            return titleEl?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Text")?.Value?.Trim() ?? "";
        }

        private static string GetNetworkLang(XElement network)
        {
            return network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ProgrammingLanguage")?.Value ?? "LAD";
        }

        private static List<string> ExtractInstructions(XElement network)
        {
            var result = new List<string>();
            var source = network.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
            if (source == null) return result;

            foreach (var el in source.Descendants())
            {
                switch (el.Name.LocalName)
                {
                    case "Contact":
                        var neg = el.Attribute("Negated")?.Value == "true";
                        result.Add($"Contact: {GetAccessName(el)}{(neg ? " [NF]" : " [NA]")}");
                        break;
                    case "Coil":
                        result.Add($"Coil: {GetAccessName(el)}");
                        break;
                    case "Call":
                        var ci = el.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                        if (ci != null)
                            result.Add($"Call: {ci.Attribute("Name")?.Value}");
                        break;
                    case "Part":
                        var pName = el.Attribute("Name")?.Value;
                        if (!string.IsNullOrEmpty(pName))
                            result.Add($"Inst: {pName}");
                        break;
                    case "Access":
                        var scope = el.Attribute("Scope")?.Value;
                        if (scope == "GlobalVariable" || scope == "LocalVariable")
                        {
                            var varName = GetAccessName(el);
                            if (!string.IsNullOrEmpty(varName))
                                result.Add($"Var: {varName}");
                        }
                        break;
                }
            }
            return result.Distinct().ToList();
        }

        private static string GetAccessName(XElement el)
        {
            var sym = el.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Symbol");
            return string.Join(".", sym?.Elements()
                .Select(c => c.Attribute("Name")?.Value ?? c.Value)
                ?? Enumerable.Empty<string>());
        }

        // ── Utilitários públicos ───────────────────────────────────────────────

        public static string StateLabel(CompareResultState state)
        {
            switch (state)
            {
                case CompareResultState.ObjectsDifferent:
                case CompareResultState.FolderContentsDifferent:
                case CompareResultState.FolderContainsDifferencesOwnStateDifferent:
                case CompareResultState.FolderContentEqualOwnStateDifferent:
                    return "DIFERENTE";
                case CompareResultState.LeftMissing:
                    return "SO NO PLC";
                case CompareResultState.RightMissing:
                    return "SO NO PROJECTO";
                case CompareResultState.ObjectsIdentical:
                case CompareResultState.FolderContentsIdentical:
                    return "IGUAL";
                default:
                    return state.ToString();
            }
        }

        public static bool IsDifferent(CompareResultState state)
        {
            return state == CompareResultState.ObjectsDifferent
                || state == CompareResultState.LeftMissing
                || state == CompareResultState.RightMissing
                || state == CompareResultState.FolderContentsDifferent
                || state == CompareResultState.FolderContainsDifferencesOwnStateDifferent
                || state == CompareResultState.FolderContentEqualOwnStateDifferent;
        }

        // ── Reconstituição de código SCL a partir do XML tokenizado ──────────

        private static string ReconstructScl(XElement structuredText)
        {
            var sb = new System.Text.StringBuilder();
            ReconstructSclChildren(structuredText.Elements(), sb);
            return sb.ToString().Trim();
        }

        private static void ReconstructSclChildren(
            IEnumerable<XElement> elements, System.Text.StringBuilder sb)
        {
            foreach (var el in elements)
            {
                switch (el.Name.LocalName)
                {
                    case "Token":
                        sb.Append(el.Attribute("Text")?.Value ?? "");
                        break;
                    case "Blank":
                        int nb = 1;
                        int.TryParse(el.Attribute("Num")?.Value, out nb);
                        sb.Append(new string(' ', nb));
                        break;
                    case "NewLine":
                        sb.Append('\n');
                        break;
                    case "LineComment":
                        var ct = el.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "Text")?.Value ?? "";
                        sb.Append("//" + ct + "\n");
                        break;
                    case "Access":
                        sb.Append(ReconstructAccess(el));
                        break;
                    default:
                        ReconstructSclChildren(el.Elements(), sb);
                        break;
                }
            }
        }

        private static string ReconstructAccess(XElement access)
        {
            var scope = access.Attribute("Scope")?.Value;
            if (scope == "LiteralConstant")
                return access.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "";

            var symbol = access.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (symbol == null) return "";

            var sb = new System.Text.StringBuilder();
            foreach (var el in symbol.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "Component":
                        var name = el.Attribute("Name")?.Value ?? "";
                        var hasQuotes = el.Elements()
                            .Any(e => e.Name.LocalName == "BooleanAttribute" &&
                                      (string)e.Attribute("Name") == "HasQuotes" &&
                                      e.Value == "true");
                        sb.Append(hasQuotes ? $"\"{name}\"" : name);
                        break;
                    case "Token":
                        sb.Append(el.Attribute("Text")?.Value ?? "");
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
