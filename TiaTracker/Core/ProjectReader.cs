using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaTracker.Core.FbdParsers;

namespace TiaTracker.Core
{
    public class MemberInfo
    {
        public string          Name         { get; set; }
        public string          DataType     { get; set; }
        public string          InitialValue { get; set; }
        public string          Comment      { get; set; }
        public List<MemberInfo> Members     { get; set; } = new List<MemberInfo>();
    }

    public class SectionInfo
    {
        public string           Name    { get; set; }
        public List<MemberInfo> Members { get; set; } = new List<MemberInfo>();
    }

    public class NetworkInfo
    {
        public int          Index    { get; set; }
        public string       Title    { get; set; }
        public string       Language { get; set; }
        public List<string> Lines    { get; set; } = new List<string>();
    }

    public class TagInfo
    {
        public string Name     { get; set; }
        public string DataType { get; set; }
        public string Address  { get; set; }
        public string Comment  { get; set; }
    }

    public class TagTableInfo
    {
        public string        Name   { get; set; }
        public string        Device { get; set; }
        public List<TagInfo> Tags   { get; set; } = new List<TagInfo>();
    }

    public class UdtInfo
    {
        public string           Name    { get; set; }
        public string           Device  { get; set; }
        public int              Number  { get; set; }
        public List<MemberInfo> Members { get; set; } = new List<MemberInfo>();
    }

    public class BlockInfo
    {
        public string            Name       { get; set; }
        public string            Type       { get; set; }
        public string            Language   { get; set; }
        public string            Device     { get; set; }
        public int               Number     { get; set; }
        public string            FolderPath { get; set; } = "";   // ex: "Motor Control/Safety"
        public string            RawXml     { get; set; } = "";   // XML bruto exportado pelo TIA
        public List<SectionInfo> Interface  { get; set; } = new List<SectionInfo>();
        public List<NetworkInfo> Networks   { get; set; } = new List<NetworkInfo>();
    }

    /// <summary>
    /// LÃª todos os blocos do projecto offline â€” sem ir online, sem modificar nada.
    /// </summary>
    public class ProjectReader
    {
        private readonly Project _project;

        public ProjectReader(Project project)
        {
            _project = project;
        }

        public List<BlockInfo> ReadAllBlocks()
        {
            var all = new List<BlockInfo>();

            foreach (Device device in GetAllDevices())
            {
                var plcSw = GetPlcSoftware(device);
                if (plcSw == null) continue;

                Console.WriteLine($"\n  PLC: {device.Name}");
                ReadBlockGroup(plcSw.BlockGroup, device.Name, all);
            }

            // Enriquecer chamadas: "CALL FC_Motor [FC]" â†’ "CALL FC5 â€” FC_Motor [FC]"
            EnrichCallReferences(all);

            return all;
        }

        private static void EnrichCallReferences(List<BlockInfo> all)
        {
            // Mapa nome â†’ bloco (para lookup rÃ¡pido)
            var nameMap = new Dictionary<string, BlockInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in all)
                if (!nameMap.ContainsKey(b.Name))
                    nameMap[b.Name] = b;

            foreach (var blk in all)
            {
                foreach (var net in blk.Networks)
                {
                    for (int i = 0; i < net.Lines.Count; i++)
                    {
                        var line = net.Lines[i];
                        // Linha tem "CALL <nome> [<tipo>]"
                        var callIdx = line.IndexOf("CALL ", StringComparison.Ordinal);
                        if (callIdx < 0) continue;

                        // Extrair o nome do bloco chamado
                        var after = line.Substring(callIdx + 5);         // apÃ³s "CALL "
                        var bracket = after.IndexOf(" [", StringComparison.Ordinal);
                        if (bracket < 0) continue;

                        var calledName = after.Substring(0, bracket).Trim();
                        if (nameMap.TryGetValue(calledName, out var calledBlock))
                        {
                            var numbered = $"{calledBlock.Type}{calledBlock.Number} â€” {calledName}";
                            net.Lines[i] = line.Substring(0, callIdx + 5) + numbered + after.Substring(bracket);
                        }
                    }
                }
            }
        }

        private void ReadBlockGroup(PlcBlockGroup group, string deviceName, List<BlockInfo> all, string folderPath = "")
        {
            foreach (PlcBlock block in group.Blocks)
            {
                var info = ReadBlock(block, deviceName, folderPath);
                if (info != null)
                {
                    all.Add(info);
                    var folder = string.IsNullOrEmpty(folderPath) ? "" : $"[{folderPath}] ";
                    Console.WriteLine($"    {folder}[{info.Type,-4}] {info.Name}  ({info.Networks.Count} redes, {info.Interface.Sum(s => s.Members.Count)} vars)");
                }
            }

            foreach (PlcBlockGroup sub in group.Groups)
            {
                var subPath = string.IsNullOrEmpty(folderPath) ? sub.Name : folderPath + "/" + sub.Name;
                ReadBlockGroup(sub, deviceName, all, subPath);
            }
        }

        private static BlockInfo ReadBlock(PlcBlock block, string deviceName, string folderPath = "")
        {
            var tempFile = Path.Combine(Path.GetTempPath(),
                BlockExporter.Sanitize(block.Name) + "_read.xml");
            try
            {
                block.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                var xml = File.ReadAllText(tempFile);
                var doc = XDocument.Parse(xml);

                var blockType = GetBlockType(block);

                // Parse block number
                int number = 0;
                var numEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Number");
                if (numEl != null) int.TryParse(numEl.Value, out number);

                // Parse language
                var langEl = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ProgrammingLanguage" &&
                                         e.Parent?.Name.LocalName == "AttributeList");
                var lang = langEl?.Value ?? "";

                // Parse interface sections
                var iface = ParseInterface(doc);

                // Parse networks (CompileUnits)
                var networks = ParseNetworks(doc);

                // For DB blocks: if no networks, expose the Static section as readable content
                if ((blockType == "DB" || blockType == "iDB") && networks.Count == 0)
                {
                    var staticSec = iface.FirstOrDefault(s => s.Name == "Static");
                    if (staticSec != null && staticSec.Members.Count > 0)
                    {
                        var net = new NetworkInfo { Index = 1, Title = "VariÃ¡veis", Language = "DB" };
                        foreach (var m in FlattenMembers(staticSec.Members, ""))
                            net.Lines.Add(m);
                        networks.Add(net);
                    }
                }

                return new BlockInfo
                {
                    Name       = block.Name,
                    Type       = blockType,
                    Language   = lang,
                    Device     = deviceName,
                    Number     = number,
                    FolderPath = folderPath,
                    RawXml     = xml,
                    Interface  = iface,
                    Networks   = networks
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    SKIP {block.Name}: {ex.Message}");
                return null;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        // â”€â”€ Interface (parÃ¢metros e variÃ¡veis do bloco) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static List<SectionInfo> ParseInterface(XDocument doc)
        {
            var result = new List<SectionInfo>();

            // The Interface element contains an XML blob with its own namespace
            var ifaceEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Interface");
            if (ifaceEl == null) return result;

            // The Sections element is the direct child (possibly with namespace)
            var sectionsEl = ifaceEl.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Sections");
            if (sectionsEl == null) return result;

            foreach (var sectionEl in sectionsEl.Elements()
                .Where(e => e.Name.LocalName == "Section"))
            {
                var sec = new SectionInfo
                {
                    Name    = sectionEl.Attribute("Name")?.Value ?? "",
                    Members = ParseMembers(sectionEl)
                };
                if (sec.Members.Count > 0 || sec.Name == "Input" || sec.Name == "Output")
                    result.Add(sec);
            }

            return result;
        }

        private static List<MemberInfo> ParseMembers(XElement parent)
        {
            var result = new List<MemberInfo>();
            foreach (var m in parent.Elements().Where(e => e.Name.LocalName == "Member"))
            {
                // Skip purely informative system members (e.g. OB input params)
                var informative = m.Attribute("Informative")?.Value == "true";

                var member = new MemberInfo
                {
                    Name     = m.Attribute("Name")?.Value ?? "",
                    DataType = m.Attribute("Datatype")?.Value ?? "",
                    Comment  = m.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "MultiLanguageText")?.Value?.Trim() ?? "",
                    Members  = ParseMembers(m)   // nested (Struct, Array, etc.)
                };

                // StartValue
                var sv = m.Descendants().FirstOrDefault(e => e.Name.LocalName == "StartValue");
                if (sv != null) member.InitialValue = sv.Value;

                if (!informative)
                    result.Add(member);
            }
            return result;
        }

        private static IEnumerable<string> FlattenMembers(List<MemberInfo> members, string prefix)
        {
            foreach (var m in members)
            {
                var fullName = string.IsNullOrEmpty(prefix) ? m.Name : $"{prefix}.{m.Name}";
                var init     = string.IsNullOrEmpty(m.InitialValue) ? "" : $" := {m.InitialValue}";
                var comment  = string.IsNullOrEmpty(m.Comment)      ? "" : $"  // {m.Comment}";
                yield return $"  {fullName} : {m.DataType}{init}{comment}";

                foreach (var sub in FlattenMembers(m.Members, fullName))
                    yield return sub;
            }
        }

        // â”€â”€ Networks (CompileUnits) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static List<NetworkInfo> ParseNetworks(XDocument doc)
        {
            var result = new List<NetworkInfo>();

            var compileUnits = doc.Descendants()
                .Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit")
                .ToList();

            for (int i = 0; i < compileUnits.Count; i++)
            {
                var cu = compileUnits[i];

                var lang = cu.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ProgrammingLanguage")?.Value ?? "LAD";

                var titleEl = cu.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "MultilingualText" &&
                        (string)e.Attribute("CompositionName") == "Title");
                var title = titleEl?.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Text")?.Value?.Trim() ?? "";

                var net = new NetworkInfo
                {
                    Index    = i + 1,
                    Title    = title,
                    Language = lang
                };

                var sourceEl = cu.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "NetworkSource");

                if (sourceEl != null && sourceEl.Elements().Any())
                {
                    if (lang == "SCL" || lang == "STL")
                    {
                        var stEl = sourceEl.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "StructuredText");
                        string body = stEl != null
                            ? ReconstructScl(stEl)
                            : sourceEl.Descendants()
                                .FirstOrDefault(e => e.Name.LocalName == "Body")?.Value?.Trim() ?? "";

                        foreach (var line in body.Split('\n')
                            .Select(l => l.TrimEnd()).Where(l => l.Length > 0))
                            net.Lines.Add(line);
                    }
                    else
                    {
                        // LAD / FBD â€” reconstruct using wire graph
                        net.Lines.AddRange(ReconstructLadFbd(sourceEl));
                    }
                }

                // Only add non-empty networks
                if (net.Lines.Count > 0 || !string.IsNullOrEmpty(net.Title))
                    result.Add(net);
            }

            return result;
        }

        // â”€â”€ LAD / FBD wire-graph reconstruction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static List<string> ReconstructLadFbd(XElement sourceEl)
        {
            var result = new List<string>();

            var flgNet = sourceEl.Elements().FirstOrDefault();
            if (flgNet == null) return result;

            // â”€â”€ 1. Build element maps (only top-level within FlgNet > Parts) â”€
            var accessMap = new Dictionary<string, XElement>();
            var partMap   = new Dictionary<string, XElement>();
            var callMap   = new Dictionary<string, XElement>();

            // Only look at direct Part/Access/Call children of the FlgNet root
            // (descendants would pick up nested UIds from Symbol etc.)
            foreach (var el in flgNet.Descendants())
            {
                var uid = el.Attribute("UId")?.Value;
                if (uid == null) continue;
                var ln = el.Name.LocalName;
                if (ln == "Access" && !accessMap.ContainsKey(uid)) accessMap[uid] = el;
                else if (ln == "Part" && !partMap.ContainsKey(uid)) partMap[uid]  = el;
                else if (ln == "Call" && !callMap.ContainsKey(uid)) callMap[uid]  = el;
            }

            // â”€â”€ 2. Build bidirectional wire maps â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // wireFrom[(dst,dstPort)] = (src,srcPort)   â† for expression building (backward)
            // wireTo  [(src,srcPort)] = list of (dst,dstPort)  â† for output discovery (forward)
            var wireFrom = new Dictionary<(string uid, string port), (string uid, string port)>();
            var wireTo   = new Dictionary<(string uid, string port), List<(string uid, string port)>>();

            // TIA Portal XML rule (confirmed from real XML analysis):
            // The SOURCE of a wire is ALWAYS the FIRST element.
            // Powerrail/OpenCon/IdentCon first â†’ they drive into NameCon destinations.
            // NameCon with output port first â†’ part output drives into next part/variable.
            foreach (var wire in flgNet.Descendants().Where(e => e.Name.LocalName == "Wire"))
            {
                var wNodes = wire.Elements().ToList();
                if (wNodes.Count < 2) continue;

                // â”€â”€ Source: always first child â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                string srcUId = null, srcPort = null;
                var first = wNodes[0];
                switch (first.Name.LocalName)
                {
                    case "Powerrail": srcUId = "PWR";  srcPort = "out"; break;
                    case "OpenCon":   srcUId = "OPEN"; srcPort = "out"; break;
                    case "IdentCon":  srcUId = first.Attribute("UId")?.Value; srcPort = "out"; break;
                    case "NameCon":   srcUId = first.Attribute("UId")?.Value;
                                      srcPort = first.Attribute("Name")?.Value; break;
                }
                if (srcUId == null) continue;

                var srcKey = (srcUId, srcPort ?? "out");
                if (!wireTo.ContainsKey(srcKey))
                    wireTo[srcKey] = new List<(string uid, string port)>();

                // â”€â”€ Destinations: all remaining children (fan-out supported) â”€
                for (int c = 1; c < wNodes.Count; c++)
                {
                    var el = wNodes[c];
                    string dstUId = null, dstPort = null;
                    switch (el.Name.LocalName)
                    {
                        case "NameCon":  dstUId = el.Attribute("UId")?.Value;
                                         dstPort = el.Attribute("Name")?.Value ?? "in"; break;
                        case "IdentCon": dstUId = el.Attribute("UId")?.Value; dstPort = "in"; break;
                        case "OpenCon":  continue; // unconnected output terminal â€” skip
                    }
                    if (dstUId == null) continue;

                    var dstKey = (dstUId, dstPort ?? "in");
                    wireFrom[dstKey] = (srcUId, srcPort);
                    if (!wireTo[srcKey].Contains(dstKey))
                        wireTo[srcKey].Add(dstKey);
                }
            }

            // â”€â”€ 3. Criar contexto compartilhado e recolher saÃ­das via sub-parsers â”€
            var ctx = new FbdContext(accessMap, partMap, callMap, wireFrom, wireTo);

            // 3a. Bit logic: bobinas, flip-flops (AND, OR, Coil, Sr, Rs, ...) â”€
            BitLogicParser.CollectOutputs(ctx, result);

            // 3b. Timers: TON, TOF, TP, TONR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            TimerParser.CollectOutputs(ctx, result);

            // 3c. Counters: CTU, CTD, CTUD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            CounterParser.CollectOutputs(ctx, result);

            // 3d. Math: Add, Sub, Mul, ... â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            MathParser.CollectOutputs(ctx, result);

            // 3e. Move: Move, FillBlk, ... â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            MoveParser.CollectOutputs(ctx, result);

            // 3f. Conversion: Convert, Round, ... â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            ConversionParser.CollectOutputs(ctx, result);

            // 3g. Word logic / Shift-Rotate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            WordLogicParser.CollectOutputs(ctx, result);

            // 3h. String operations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            StringParser.CollectOutputs(ctx, result);

            // 3i. Program control: Calculate, TypeConvert â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            ProgramControlParser.CollectOutputs(ctx, result);

            // 3j. FC/FB Calls â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            foreach (var kv in callMap)
            {
                var uid       = kv.Key;
                var ci        = kv.Value.Descendants().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                var blockName = ci?.Attribute("Name")?.Value ?? "?";
                var blockType = ci?.Attribute("BlockType")?.Value ?? "";
                var instName  = FbdContext.GetCallInstanceName(kv.Value) ?? blockName;

                var header = instName != blockName
                    ? $"CALL {blockName} [{blockType}]  instance: {instName}"
                    : $"CALL {blockName} [{blockType}]";

                // CondiÃ§Ã£o EN
                string enCond = "";
                if (wireFrom.TryGetValue((uid, "en"), out var enSrc) &&
                    enSrc.uid != "PWR" && enSrc.uid != "OPEN")
                    enCond = ctx.ResolveNode(enSrc.uid, enSrc.port, 0);
                if (!string.IsNullOrEmpty(enCond))
                    header = $"IF {enCond}: {header}";

                result.Add(header);

                // SecÃ§Ãµes de parÃ¢metros (Input/Output/InOut)
                var paramSections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (ci != null)
                    foreach (var p in ci.Elements().Where(e => e.Name.LocalName == "Parameter"))
                    {
                        var pName = p.Attribute("Name")?.Value;
                        var pSec  = p.Attribute("Section")?.Value ?? "Input";
                        if (pName != null) paramSections[pName] = pSec;
                    }

                // ParÃ¢metros de entrada (wires para dentro do call)
                foreach (var pk in wireFrom.Keys
                    .Where(k => k.uid == uid && k.port != "en" && k.port != "eno")
                    .OrderBy(k => k.port))
                {
                    var src   = wireFrom[pk];
                    var val   = ctx.ResolveNode(src.uid, src.port, 0);
                    var sec   = paramSections.TryGetValue(pk.port, out var s) ? s : "Input";
                    var label = sec == "InOut" ? "INOUT" : sec == "Output" ? "OUT" : "IN";
                    result.Add($"  {label,-5} {pk.port} := {val}");
                }

                // ParÃ¢metros de saÃ­da (wires para fora do call)
                foreach (var pk in wireTo.Keys
                    .Where(k => k.uid == uid && k.port != "eno")
                    .OrderBy(k => k.port))
                {
                    paramSections.TryGetValue(pk.port, out var pSec);
                    if (pSec == "InOut") continue;
                    foreach (var dst in wireTo[pk])
                    {
                        var destName = ctx.ResolveDestination(dst.uid, dst.port);
                        result.Add($"  {"OUT",-5} {pk.port} => {destName}");
                    }
                }
            }

            // 3k. Partes genÃ©ricas: saÃ­da vai diretamente para variÃ¡vel (sem Coil) â”€
            // (Partes jÃ¡ tratadas pelos sub-parsers acima sÃ£o ignoradas aqui)
            var seen3k = new HashSet<string>();
            foreach (var kv in partMap)
            {
                var uid      = kv.Key;
                var partName = kv.Value.Attribute("Name")?.Value ?? "";

                // Pular partes jÃ¡ tratadas por BitLogicParser.CollectOutputs
                switch (partName)
                {
                    case "Coil": case "SCoil": case "RCoil": case "PCoil": case "NCoil":
                    case "Sr":   case "Rs":                    case "R_TRIG": case "F_TRIG":                        continue;
                }

                // CondiÃ§Ã£o EN
                string enCond = "";
                if (wireFrom.TryGetValue((uid, "en"), out var enWire) &&
                    enWire.uid != "PWR" && enWire.uid != "OPEN")
                    enCond = ctx.ResolveNode(enWire.uid, enWire.port, 0);
                bool alwaysOn = string.IsNullOrEmpty(enCond);

                // Qualquer porta de saÃ­da que vai para uma variÃ¡vel (Access)
                foreach (var outKey in wireTo.Keys.Where(k => k.uid == uid))
                {
                    if (outKey.port == "eno") continue;
                    foreach (var dst in wireTo[outKey])
                    {
                        if (!accessMap.ContainsKey(dst.uid)) continue;

                        var varName = ctx.GetVarName(accessMap[dst.uid]);
                        var expr    = ctx.ResolveNode(uid, outKey.port, 0);
                        var line    = alwaysOn
                            ? $"{varName} := {expr}"
                            : $"IF {enCond} THEN {varName} := {expr}";

                        if (seen3k.Add(line))
                            result.Add(line);
                    }
                }
            }

            // â”€â”€ 4. Fallback: se sem saÃ­das, descreve a rede em bruto â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (result.Count == 0)
            {
                var seen = new HashSet<string>();
                foreach (var acc in accessMap.Values)
                {
                    var scope = acc.Attribute("Scope")?.Value ?? "";
                    if (scope != "GlobalVariable" && scope != "LocalVariable" &&
                        scope != "LiteralConstant" && scope != "TypedConstant") continue;
                    var n = ctx.GetVarName(acc);
                    if (!string.IsNullOrEmpty(n) && n != "?" && seen.Add(n))
                        result.Add($"Var   {n}");
                }
                foreach (var part in partMap.Values)
                {
                    var pn = part.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(pn)) result.Add($"Part  [{pn}]");
                }
                foreach (var call in callMap.Values)
                {
                    var ci = call.Descendants().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                    var cn = ci?.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(cn)) result.Add($"Call  [{cn}]");
                }
            }

            return result;
        }
        // â”€â”€ SCL reconstruction from tokenized XML â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string ReconstructScl(XElement structuredText)
        {
            var sb = new StringBuilder();
            ReconstructSclChildren(structuredText.Elements(), sb);
            return sb.ToString().Trim();
        }

        private static void ReconstructSclChildren(IEnumerable<XElement> elements, StringBuilder sb)
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
                        sb.Append(ReconstructSclAccess(el));
                        break;
                    default:
                        ReconstructSclChildren(el.Elements(), sb);
                        break;
                }
            }
        }

        private static string ReconstructSclAccess(XElement access)
        {
            var scope = access.Attribute("Scope")?.Value;
            if (scope == "LiteralConstant" || scope == "TypedConstant")
                return access.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "";

            var symbol = access.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (symbol == null) return "";

            var sb = new StringBuilder();
            foreach (var el in symbol.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "Component":
                    {
                        var compName  = el.Attribute("Name")?.Value ?? "";
                        var modifier  = el.Attribute("AccessModifier")?.Value ?? "";
                        var hasQuotes = el.Elements()
                            .Any(e => e.Name.LocalName == "BooleanAttribute" &&
                                      e.Attribute("Name")?.Value == "HasQuotes" &&
                                      e.Value == "true");
                        sb.Append(hasQuotes ? $"\"{compName}\"" : compName);
                        // Handle array indices: Component children that are Access elements
                        if (modifier == "Array" || el.Elements().Any(e => e.Name.LocalName == "Access"))
                        {
                            var indices = el.Elements()
                                .Where(e => e.Name.LocalName == "Access")
                                .Select(idxAcc =>
                                {
                                    var cv = idxAcc.Descendants()
                                        .FirstOrDefault(e2 => e2.Name.LocalName == "ConstantValue")?.Value;
                                    if (cv != null) return cv;
                                    var idxSym = idxAcc.Descendants().FirstOrDefault(e2 => e2.Name.LocalName == "Symbol");
                                    return idxSym != null ? FbdContext.GetVarNameFromSymbol(idxSym) : "?";
                                })
                                .ToList();
                            if (indices.Count > 0)
                                sb.Append($"[{string.Join(", ", indices)}]");
                        }
                        break;
                    }
                    case "Token":
                        sb.Append(el.Attribute("Text")?.Value ?? "");
                        break;
                }
            }
            return sb.ToString();
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string GetBlockType(PlcBlock block)
        {
            if (block is OB)         return "OB";
            if (block is FB)         return "FB";
            if (block is FC)         return "FC";
            if (block is GlobalDB)   return "DB";
            if (block is InstanceDB) return "iDB";
            return "BLK";
        }

        private IEnumerable<Device> GetAllDevices()
        {
            foreach (Device d in _project.Devices) yield return d;
            foreach (DeviceGroup g in _project.DeviceGroups)
                foreach (Device d in g.Devices) yield return d;
        }

        private static PlcSoftware GetPlcSoftware(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc) return plc;
            }
            return null;
        }

        // â”€â”€ Tag Tables â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public List<TagTableInfo> ReadAllTagTables()
        {
            var all = new List<TagTableInfo>();
            foreach (Device device in GetAllDevices())
            {
                var plcSw = GetPlcSoftware(device);
                if (plcSw == null) continue;
                ReadTagTableGroup(plcSw.TagTableGroup, device.Name, all);
            }
            return all;
        }

        private void ReadTagTableGroup(PlcTagTableGroup group, string deviceName, List<TagTableInfo> all)
        {
            foreach (PlcTagTable table in group.TagTables)
            {
                var info = ReadTagTable(table, deviceName);
                if (info != null) all.Add(info);
            }
            foreach (PlcTagTableGroup sub in group.Groups)
                ReadTagTableGroup(sub, deviceName, all);
        }

        private static TagTableInfo ReadTagTable(PlcTagTable table, string deviceName)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), BlockExporter.Sanitize(table.Name) + "_tags.xml");
            try
            {
                table.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                var doc  = XDocument.Load(tempFile);
                var info = new TagTableInfo { Name = table.Name, Device = deviceName };

                foreach (var tagEl in doc.Descendants().Where(e => e.Name.LocalName == "SW.Tags.PlcTag"))
                {
                    var attrs = tagEl.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
                    if (attrs == null) continue;
                    var tag = new TagInfo
                    {
                        Name     = attrs.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "",
                        DataType = attrs.Elements().FirstOrDefault(e => e.Name.LocalName == "DataTypeName")?.Value ?? "",
                        Address  = attrs.Elements().FirstOrDefault(e => e.Name.LocalName == "LogicalAddress")?.Value ?? "",
                        Comment  = attrs.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value ?? ""
                    };
                    if (!string.IsNullOrEmpty(tag.Name)) info.Tags.Add(tag);
                }

                Console.WriteLine($"    [TAGS] {info.Name}  ({info.Tags.Count} tags)");
                return info;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    SKIP tags {table.Name}: {ex.Message}");
                return null;
            }
            finally { try { File.Delete(tempFile); } catch { } }
        }

        // â”€â”€ UDTs (PLC Data Types) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public List<UdtInfo> ReadAllUDTs()
        {
            var all = new List<UdtInfo>();
            try
            {
                foreach (Device device in GetAllDevices())
                {
                    var plcSw = GetPlcSoftware(device);
                    if (plcSw == null) continue;
                    ReadUdtGroup(plcSw.TypeGroup, device.Name, all);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  SKIP UDTs: {ex.Message}");
            }
            return all;
        }

        private void ReadUdtGroup(PlcTypeGroup group, string deviceName, List<UdtInfo> all)
        {
            foreach (PlcType udt in group.Types)
            {
                var info = ReadUdt(udt, deviceName);
                if (info != null) all.Add(info);
            }
            foreach (PlcTypeGroup sub in group.Groups)
                ReadUdtGroup(sub, deviceName, all);
        }

        private static UdtInfo ReadUdt(PlcType udt, string deviceName)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), BlockExporter.Sanitize(udt.Name) + "_udt.xml");
            try
            {
                udt.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                var doc  = XDocument.Load(tempFile);
                var info = new UdtInfo { Name = udt.Name, Device = deviceName };

                // Number
                var numEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Number");
                if (numEl != null) int.TryParse(numEl.Value, out int n);

                // Members from Interface
                var iface = ParseInterface(doc);
                var body  = iface.FirstOrDefault(s => s.Name == "None" || s.Name == "") ?? iface.FirstOrDefault();
                if (body != null) info.Members = body.Members;

                Console.WriteLine($"    [UDT]  {info.Name}  ({info.Members.Count} members)");
                return info;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    SKIP UDT {udt.Name}: {ex.Message}");
                return null;
            }
            finally { try { File.Delete(tempFile); } catch { } }
        }
    }
}
