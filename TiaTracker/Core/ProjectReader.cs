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
    /// Lê todos os blocos do projecto offline — sem ir online, sem modificar nada.
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

            // Enriquecer chamadas: "CALL FC_Motor [FC]" → "CALL FC5 — FC_Motor [FC]"
            EnrichCallReferences(all);

            return all;
        }

        private static void EnrichCallReferences(List<BlockInfo> all)
        {
            // Mapa nome → bloco (para lookup rápido)
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
                        var after = line.Substring(callIdx + 5);         // após "CALL "
                        var bracket = after.IndexOf(" [", StringComparison.Ordinal);
                        if (bracket < 0) continue;

                        var calledName = after.Substring(0, bracket).Trim();
                        if (nameMap.TryGetValue(calledName, out var calledBlock))
                        {
                            var numbered = $"{calledBlock.Type}{calledBlock.Number} — {calledName}";
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
                        var net = new NetworkInfo { Index = 1, Title = "Variáveis", Language = "DB" };
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

        // ── Interface (parâmetros e variáveis do bloco) ───────────────────────

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

        // ── Networks (CompileUnits) ───────────────────────────────────────────

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
                        // LAD / FBD — reconstruct using wire graph
                        net.Lines.AddRange(ReconstructLadFbd(sourceEl));
                    }
                }

                // Only add non-empty networks
                if (net.Lines.Count > 0 || !string.IsNullOrEmpty(net.Title))
                    result.Add(net);
            }

            return result;
        }

        // ── LAD / FBD wire-graph reconstruction ───────────────────────────────

        private static List<string> ReconstructLadFbd(XElement sourceEl)
        {
            var result = new List<string>();

            var flgNet = sourceEl.Elements().FirstOrDefault();
            if (flgNet == null) return result;

            // ── 1. Build element maps (only top-level within FlgNet > Parts) ─
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

            // ── 2. Build bidirectional wire maps ──────────────────────────────
            // wireFrom[(dst,dstPort)] = (src,srcPort)   ← for expression building (backward)
            // wireTo  [(src,srcPort)] = list of (dst,dstPort)  ← for output discovery (forward)
            var wireFrom = new Dictionary<(string uid, string port), (string uid, string port)>();
            var wireTo   = new Dictionary<(string uid, string port), List<(string uid, string port)>>();

            foreach (var wire in flgNet.Descendants().Where(e => e.Name.LocalName == "Wire"))
            {
                var wNodes = wire.Elements().ToList();
                if (wNodes.Count < 2) continue;

                // Determine source by SEMANTICS, not position.
                // TIA Portal can place DESTINATION first in the Wire element list.
                // Priority 1: Powerrail  2: NameCon with output-type port  3: first IdentCon
                string srcUId = null, srcPort = null;

                if (wNodes.Any(n => n.Name.LocalName == "Powerrail"))
                {
                    srcUId = "PWR"; srcPort = "out";
                }
                else
                {
                    // Look for NameCon whose port name is an output-type
                    foreach (var n in wNodes.Where(n => n.Name.LocalName == "NameCon"))
                    {
                        var p = n.Attribute("Name")?.Value ?? "";
                        if (IsOutputPort(p))
                        {
                            srcUId = n.Attribute("UId")?.Value;
                            srcPort = p;
                            if (srcUId != null) break;
                        }
                    }
                    // Fallback: first IdentCon is the source (variable driving into Part)
                    if (srcUId == null)
                    {
                        var ic = wNodes.FirstOrDefault(n => n.Name.LocalName == "IdentCon");
                        if (ic != null) { srcUId = ic.Attribute("UId")?.Value; srcPort = "out"; }
                    }
                    // Last fallback: first NameCon regardless of port name
                    if (srcUId == null)
                    {
                        var nc = wNodes.FirstOrDefault(n => n.Name.LocalName == "NameCon");
                        if (nc != null) { srcUId = nc.Attribute("UId")?.Value; srcPort = nc.Attribute("Name")?.Value ?? "out"; }
                    }
                }
                if (srcUId == null) continue;

                var srcKey = (srcUId, srcPort ?? "out");
                if (!wireTo.ContainsKey(srcKey))
                    wireTo[srcKey] = new List<(string uid, string port)>();

                // All non-source elements are destinations
                foreach (var n in wNodes)
                {
                    string dstUId = null, dstPort = null;
                    switch (n.Name.LocalName)
                    {
                        case "Powerrail": continue; // already handled as source
                        case "OpenCon":   continue; // open endpoint — skip
                        case "IdentCon":
                            dstUId = n.Attribute("UId")?.Value;
                            if (dstUId == srcUId) continue; // skip — this is the source
                            dstPort = "in";
                            break;
                        case "NameCon":
                            dstUId = n.Attribute("UId")?.Value;
                            dstPort = n.Attribute("Name")?.Value ?? "in";
                            if (dstUId == srcUId && dstPort == srcPort) continue; // skip — this is the source
                            break;
                    }
                    if (dstUId == null) continue;

                    var dstKey = (dstUId, dstPort ?? "in");
                    wireFrom[dstKey] = (srcUId, srcPort);
                    if (!wireTo[srcKey].Contains(dstKey))
                        wireTo[srcKey].Add(dstKey);
                }
            }

            // ── 3. Find ALL output points and generate lines ───────────────────

            // 3a. Coils e flip-flops de saída (LAD/FBD)
            foreach (var kv in partMap)
            {
                var partName = kv.Value.Attribute("Name")?.Value ?? "";
                bool isCoilType = partName == "Coil"  || partName == "SCoil" || partName == "RCoil"
                               || partName == "PCoil" || partName == "NCoil"
                               || partName == "Sr"    || partName == "Rs";   // Set/Reset flip-flops
                if (!isCoilType) continue;

                var uid = kv.Key;

                // ── Flip-flops Sr / Rs — lógica especial com portas S/R1 e S1/R ──
                if (partName == "Sr" || partName == "Rs")
                {
                    // Operand (InOut) — variável controlada
                    string ffOperand = "";
                    if (wireFrom.TryGetValue((uid, "operand"), out var ffOpSrc))
                        ffOperand = ResolveNode(ffOpSrc.uid, ffOpSrc.port, accessMap, partMap, callMap, wireFrom, 0);
                    if (string.IsNullOrEmpty(ffOperand)) ffOperand = "?";

                    if (partName == "Sr")
                    {
                        // Sr: R1 tem prioridade sobre S
                        string sIn  = "(sem ligação)";
                        string r1In = "(sem ligação)";
                        if (wireFrom.TryGetValue((uid, "S"),  out var sSrc))  sIn  = ResolveNode(sSrc.uid,  sSrc.port,  accessMap, partMap, callMap, wireFrom, 0);
                        if (wireFrom.TryGetValue((uid, "R1"), out var r1Src)) r1In = ResolveNode(r1Src.uid, r1Src.port, accessMap, partMap, callMap, wireFrom, 0);
                        result.Add($"SR flip-flop  {ffOperand}:  // R1 tem prioridade");
                        result.Add($"  S  := {sIn}");
                        result.Add($"  R1 := {r1In}");
                    }
                    else // Rs
                    {
                        // Rs: S1 tem prioridade sobre R
                        string s1In = "(sem ligação)";
                        string rIn  = "(sem ligação)";
                        if (wireFrom.TryGetValue((uid, "S1"), out var s1Src)) s1In = ResolveNode(s1Src.uid, s1Src.port, accessMap, partMap, callMap, wireFrom, 0);
                        if (wireFrom.TryGetValue((uid, "R"),  out var rSrc))  rIn  = ResolveNode(rSrc.uid,  rSrc.port,  accessMap, partMap, callMap, wireFrom, 0);
                        result.Add($"RS flip-flop  {ffOperand}:  // S1 tem prioridade");
                        result.Add($"  S1 := {s1In}");
                        result.Add($"  R  := {rIn}");
                    }
                    continue;
                }

                // ── Coils normais ─────────────────────────────────────────────────
                string operand = "";
                if (wireFrom.TryGetValue((uid, "operand"), out var opSrc))
                    operand = ResolveNode(opSrc.uid, opSrc.port, accessMap, partMap, callMap, wireFrom, 0);

                string condition = "(PowerRail)";
                if (wireFrom.TryGetValue((uid, "in"), out var inSrc))
                    condition = ResolveNode(inSrc.uid, inSrc.port, accessMap, partMap, callMap, wireFrom, 0);

                if (string.IsNullOrEmpty(operand)) continue;

                bool always = condition == "(PowerRail)" || condition == "TRUE" || string.IsNullOrEmpty(condition);
                switch (partName)
                {
                    case "SCoil": result.Add(always ? $"SET   {operand}"   : $"IF {condition} THEN SET {operand}");   break;
                    case "RCoil": result.Add(always ? $"RESET {operand}"   : $"IF {condition} THEN RESET {operand}"); break;
                    case "PCoil": result.Add(always ? $"P_SET {operand}"   : $"IF ↑{condition} THEN SET {operand}");  break;
                    case "NCoil": result.Add(always ? $"N_SET {operand}"   : $"IF ↓{condition} THEN SET {operand}");  break;
                    default:      result.Add(always ? $"{operand} := TRUE"  : $"{operand} := {condition}");            break;
                }
            }

            // 3b. Block CALLs — show instance name, all IN params and all OUT params
            foreach (var kv in callMap)
            {
                var uid         = kv.Key;
                var ci          = kv.Value.Descendants().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
                var blockName   = ci?.Attribute("Name")?.Value ?? "?";
                var blockType   = ci?.Attribute("BlockType")?.Value ?? "";
                var instName    = GetCallDisplayName(kv.Value);
                // If instance name equals block name there's no named instance (inline)
                var header      = instName != blockName
                    ? $"CALL {blockName} [{blockType}]  instance: {instName}"
                    : $"CALL {blockName} [{blockType}]";

                // EN condition
                string enCond = "(PowerRail)";
                if (wireFrom.TryGetValue((uid, "en"), out var enSrc))
                    enCond = ResolveNode(enSrc.uid, enSrc.port, accessMap, partMap, callMap, wireFrom, 0);
                if (enCond != "(PowerRail)" && enCond != "TRUE")
                    header = $"IF {enCond}: {header}";

                result.Add(header);

                // Parse parameter sections from CallInfo to know Input/Output/InOut
                var paramSections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (ci != null)
                    foreach (var p in ci.Elements().Where(e => e.Name.LocalName == "Parameter"))
                    {
                        var pName = p.Attribute("Name")?.Value;
                        var pSec  = p.Attribute("Section")?.Value ?? "Input";
                        if (pName != null) paramSections[pName] = pSec;
                    }

                // Input and InOut parameters (wires INTO the call)
                var inPorts = wireFrom.Keys
                    .Where(k => k.uid == uid && k.port != "en" && k.port != "eno")
                    .OrderBy(k => k.port)
                    .ToList();
                foreach (var pk in inPorts)
                {
                    var src  = wireFrom[pk];
                    var val  = ResolveNode(src.uid, src.port, accessMap, partMap, callMap, wireFrom, 0);
                    var sec  = paramSections.TryGetValue(pk.port, out var s) ? s : "Input";
                    var label = sec == "InOut" ? "INOUT" : sec == "Output" ? "OUT" : "IN";
                    result.Add($"  {label,-5} {pk.port} := {val}");
                }

                // Output parameters (wires OUT of the call → where they go)
                var outPorts = wireTo.Keys
                    .Where(k => k.uid == uid && k.port != "eno")
                    .OrderBy(k => k.port)
                    .ToList();
                foreach (var pk in outPorts)
                {
                    // Skip ports already shown as InOut above (they appear in both wireFrom and wireTo)
                    paramSections.TryGetValue(pk.port, out var pSec);
                    if (pSec == "InOut") continue;

                    foreach (var dst in wireTo[pk])
                    {
                        string destName = ResolveDestination(dst.uid, dst.port, accessMap, partMap);
                        result.Add($"  {"OUT",-5} {pk.port} => {destName}");
                    }
                }
            }

            // 3c. Parts whose OUTPUT goes to an Access (variable write without Coil)
            //     Also handles EN-guarded operations: e.g. AND → ADD.en, ADD.out → variable
            var seen3c = new HashSet<string>();
            foreach (var kv in partMap)
            {
                var partName = kv.Value.Attribute("Name")?.Value ?? "";
                bool isCoilType = partName == "Coil" || partName == "SCoil" || partName == "RCoil"
                               || partName == "PCoil" || partName == "NCoil"
                               || partName == "Sr"    || partName == "Rs";
                if (isCoilType) continue;

                var uid = kv.Key;

                // Resolve EN condition once for this Part
                string enCond = "(PowerRail)";
                if (wireFrom.TryGetValue((uid, "en"), out var enWire))
                    enCond = ResolveNode(enWire.uid, enWire.port, accessMap, partMap, callMap, wireFrom, 0);
                bool alwaysOn = enCond == "(PowerRail)" || enCond == "TRUE" || string.IsNullOrEmpty(enCond);

                // Check every output port of this Part
                var outKeys = wireTo.Keys.Where(k => k.uid == uid).ToList();
                foreach (var outKey in outKeys)
                {
                    if (outKey.port == "eno") continue;
                    foreach (var dst in wireTo[outKey])
                    {
                        if (!accessMap.ContainsKey(dst.uid)) continue;

                        var varName = GetVarName(accessMap[dst.uid]);
                        var expr    = ResolvePart(uid, kv.Value, accessMap, partMap, callMap, wireFrom, 0);

                        var line = alwaysOn
                            ? $"{varName} := {expr}"
                            : $"IF {enCond} THEN {varName} := {expr}";

                        if (seen3c.Add(line))   // avoid duplicates
                            result.Add(line);
                    }
                }
            }

            // ── 4. Fallback: if no outputs found, describe the network raw ────
            if (result.Count == 0)
            {
                // List all variables referenced
                var seen = new HashSet<string>();
                foreach (var acc in accessMap.Values)
                {
                    var scope = acc.Attribute("Scope")?.Value ?? "";
                    if (scope != "GlobalVariable" && scope != "LocalVariable" &&
                        scope != "LiteralConstant" && scope != "TypedConstant") continue;
                    var n = GetVarName(acc);
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

        /// <summary>Resolves the name/description of a wire DESTINATION element.</summary>
        private static string ResolveDestination(string uid, string port,
            Dictionary<string, XElement> accessMap,
            Dictionary<string, XElement> partMap)
        {
            if (accessMap.TryGetValue(uid, out var acc))
                return GetVarName(acc);
            if (partMap.TryGetValue(uid, out var part))
                return $"[{part.Attribute("Name")?.Value}].{port}";
            return $"#{uid}.{port}";
        }

        /// <summary>Resolves a wire source node to a human-readable expression.</summary>
        private static string ResolveNode(
            string uid, string port,
            Dictionary<string, XElement> accessMap,
            Dictionary<string, XElement> partMap,
            Dictionary<string, XElement> callMap,
            Dictionary<(string uid, string port), (string uid, string port)> wireFrom,
            int depth)
        {
            if (depth > 12) return "...";
            if (uid == "PWR")  return "(PowerRail)";
            if (uid == "OPEN") return "OPEN";

            // Access element = variable or constant
            if (accessMap.TryGetValue(uid, out var acc))
            {
                var scope = acc.Attribute("Scope")?.Value;
                if (scope == "LiteralConstant")
                    return acc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "?";
                if (scope == "TypedConstant")
                    return acc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "?";
                return GetVarName(acc);
            }

            // Part element = logic gate / function
            if (partMap.TryGetValue(uid, out var part))
                return ResolvePart(uid, part, accessMap, partMap, callMap, wireFrom, depth + 1);

            // Call element = block call — return "InstanceName.OutputPort" or "BlockName.OutputPort"
            if (callMap.TryGetValue(uid, out var call))
            {
                var displayName = GetCallDisplayName(call);
                return !string.IsNullOrEmpty(port) ? $"{displayName}.{port}" : displayName;
            }

            return "?";
        }

        /// <summary>Returns "InstanceName" if available, otherwise "BlockName".</summary>
        private static string GetCallDisplayName(XElement callEl)
        {
            var ci = callEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
            if (ci == null) return "?";

            var blockName = ci.Attribute("Name")?.Value ?? "?";

            // Instance DB name lives in <Instance> or <CallInstance> child of CallInfo
            var instEl = ci.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Instance" ||
                                     e.Name.LocalName == "CallInstance");
            if (instEl != null)
            {
                var sym = instEl.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Symbol");
                if (sym != null)
                {
                    var instName = GetVarNameFromSymbol(sym);
                    if (!string.IsNullOrEmpty(instName) && instName != "?")
                        return instName;
                }
            }
            return blockName;
        }

        /// <summary>Returns true if the named port is an output-type port (produces a value).</summary>
        private static bool IsOutputPort(string portName)
        {
            if (string.IsNullOrEmpty(portName)) return false;
            // Exact output port names used by TIA Portal instructions
            switch (portName.ToLowerInvariant())
            {
                case "out": case "eno": case "q": case "qu": case "qd":
                case "et": case "cv": case "ret_val": case "result":
                    return true;
            }
            // Named output ports like "out1", "out2", "outvalue" etc.
            if (portName.StartsWith("out", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Reads instance name from a timer/counter/function-block Part element.</summary>
        private static string GetPartInstanceName(XElement part)
        {
            var instEl = part.Elements().FirstOrDefault(e => e.Name.LocalName == "Instance");
            if (instEl == null) return null;
            // Instance may contain Component directly or a Symbol hierarchy
            var sym = instEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym != null) return GetVarNameFromSymbol(sym);
            var comp = instEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Component");
            return comp?.Attribute("Name")?.Value;
        }

        private static string GetVarNameFromSymbol(XElement sym)
        {
            var sb = new StringBuilder();
            bool lastWasComponent = false;
            foreach (var el in sym.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "Component":
                        var compName  = el.Attribute("Name")?.Value ?? "";
                        var hasQuotes = el.Elements().Any(e => e.Name.LocalName == "BooleanAttribute" &&
                                                                e.Attribute("Name")?.Value == "HasQuotes" &&
                                                                e.Value == "true");
                        if (lastWasComponent && sb.Length > 0) sb.Append('.');
                        sb.Append(hasQuotes ? $"\"{compName}\"" : compName);
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

        private static string ResolvePart(
            string uid, XElement part,
            Dictionary<string, XElement> accessMap,
            Dictionary<string, XElement> partMap,
            Dictionary<string, XElement> callMap,
            Dictionary<(string uid, string port), (string uid, string port)> wireFrom,
            int depth)
        {
            var name = part.Attribute("Name")?.Value ?? "?";

            // Get cardinality for multi-input gates
            int card = 2;
            var cardEl = part.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TemplateValue" &&
                                     e.Attribute("Name")?.Value == "Card");
            if (cardEl != null) int.TryParse(cardEl.Value, out card);

            // Negated input ports: <Negated Name="in2" /> means that input is inverted
            var negatedPorts = new HashSet<string>(
                part.Elements().Where(e => e.Name.LocalName == "Negated")
                    .Select(e => e.Attribute("Name")?.Value ?? "")
                    .Where(s => s.Length > 0));

            // Helper: resolve one named input port, applying negation if needed
            string Inp(string portName)
            {
                if (!wireFrom.TryGetValue((uid, portName), out var s)) return "?";
                var v = ResolveNode(s.uid, s.port, accessMap, partMap, callMap, wireFrom, depth);
                return negatedPorts.Contains(portName) ? $"NOT({v})" : v;
            }

            switch (name)
            {
                // ── Boolean logic ──────────────────────────────────────────────
                case "A":  // AND
                {
                    var inputs = CollectInputsWithNegation(uid, card, negatedPorts, accessMap, partMap, callMap, wireFrom, depth);
                    if (inputs.Count == 0) return "AND(?)";
                    for (int i = 0; i < inputs.Count; i++)
                        if (inputs[i].Contains(" OR ") || inputs[i].Contains(" XOR "))
                            inputs[i] = $"({inputs[i]})";
                    return string.Join(" AND ", inputs);
                }
                case "O":  // OR
                {
                    var inputs = CollectInputsWithNegation(uid, card, negatedPorts, accessMap, partMap, callMap, wireFrom, depth);
                    return inputs.Count == 0 ? "OR(?)" : string.Join(" OR ", inputs);
                }
                case "X":  // XOR
                {
                    var inputs = CollectInputsWithNegation(uid, card, negatedPorts, accessMap, partMap, callMap, wireFrom, depth);
                    return inputs.Count == 0 ? "XOR(?)" : string.Join(" XOR ", inputs);
                }
                case "NOT":
                    return $"NOT({Inp("in")})";

                case "Contact":
                {
                    if (!wireFrom.TryGetValue((uid, "operand"), out var src)) return "Contact(?)";
                    var v   = ResolveNode(src.uid, src.port, accessMap, partMap, callMap, wireFrom, depth);
                    bool ng = part.Attribute("Negated")?.Value == "true" || negatedPorts.Contains("operand");
                    var contactExpr = ng ? $"NOT({v})" : v;
                    // LAD series: if "in" is not PowerRail, chain previous condition with AND
                    if (wireFrom.TryGetValue((uid, "in"), out var inSrc) &&
                        inSrc.uid != "PWR" && inSrc.uid != "OPEN")
                    {
                        var prev = ResolveNode(inSrc.uid, inSrc.port, accessMap, partMap, callMap, wireFrom, depth);
                        if (!string.IsNullOrEmpty(prev) && prev != "(PowerRail)")
                        {
                            if (prev.Contains(" OR "))         prev         = $"({prev})";
                            if (contactExpr.Contains(" OR "))  contactExpr  = $"({contactExpr})";
                            return $"{prev} AND {contactExpr}";
                        }
                    }
                    return contactExpr;
                }
                case "Coil": case "SCoil": case "RCoil": case "PCoil": case "NCoil":
                    return "[COIL]"; // handled at a higher level

                // ── Data move / fill ───────────────────────────────────────────
                case "Move":
                    return $"MOVE({Inp("in")})";
                case "Swap":
                    return $"SWAP({Inp("in")})";
                case "Fill":
                    return $"FILL(IN:={Inp("IN")}, COUNT:={Inp("COUNT")})";

                // ── Arithmetic ─────────────────────────────────────────────────
                case "Add": return $"({Inp("in1")} + {Inp("in2")})";
                case "Sub": return $"({Inp("in1")} - {Inp("in2")})";
                case "Mul": return $"({Inp("in1")} * {Inp("in2")})";
                case "Div": return $"({Inp("in1")} / {Inp("in2")})";
                case "Mod": return $"({Inp("in1")} MOD {Inp("in2")})";
                case "Neg": return $"(-{Inp("in")})";
                case "Abs": return $"ABS({Inp("in")})";

                // ── Math functions ─────────────────────────────────────────────
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

                // ── Comparison ─────────────────────────────────────────────────
                case "CmpEQ": case "EQ":  return $"({Inp("in1")} = {Inp("in2")})";
                case "CmpNE": case "NE":  return $"({Inp("in1")} <> {Inp("in2")})";
                case "CmpGT": case "GT":  return $"({Inp("in1")} > {Inp("in2")})";
                case "CmpGE": case "GE":  return $"({Inp("in1")} >= {Inp("in2")})";
                case "CmpLT": case "LT":  return $"({Inp("in1")} < {Inp("in2")})";
                case "CmpLE": case "LE":  return $"({Inp("in1")} <= {Inp("in2")})";
                case "Cmp":
                {
                    // Generic comparator: try in1/in2, fall back to IN1/IN2
                    var a = wireFrom.ContainsKey((uid, "in1")) ? Inp("in1") : Inp("IN1");
                    var b = wireFrom.ContainsKey((uid, "in2")) ? Inp("in2") : Inp("IN2");
                    return $"CMP({a}, {b})";
                }

                // ── Selection / Limit ──────────────────────────────────────────
                case "Sel":    return $"SEL(G:={Inp("G")}, IN0:={Inp("IN0")}, IN1:={Inp("IN1")})";
                case "Mux":    return $"MUX(K:={Inp("K")}, ...)";
                case "DeMux":  return $"DEMUX(K:={Inp("K")}, IN:={Inp("IN")})";
                case "Limit":  return $"LIMIT(MN:={Inp("MN")}, IN:={Inp("IN")}, MX:={Inp("MX")})";
                case "Min":    return $"MIN({Inp("IN1")}, {Inp("IN2")})";
                case "Max":    return $"MAX({Inp("IN1")}, {Inp("IN2")})";

                // ── Conversion ────────────────────────────────────────────────
                case "Convert":   return $"CONVERT({Inp("in")})";
                case "Round":     return $"ROUND({Inp("in")})";
                case "Trunc":     return $"TRUNC({Inp("in")})";
                case "Ceiling":   return $"CEIL({Inp("in")})";
                case "Floor":     return $"FLOOR({Inp("in")})";
                case "Scale":     return $"SCALE(IN:={Inp("IN")})";
                case "Normalize": return $"NORM(MIN:={Inp("MIN")}, VAL:={Inp("VAL")}, MAX:={Inp("MAX")})";

                // ── Bit operations ────────────────────────────────────────────
                case "And": return $"({Inp("in1")} & {Inp("in2")})";  // bitwise AND
                case "Or":  return $"({Inp("in1")} | {Inp("in2")})";  // bitwise OR
                case "Xor": return $"({Inp("in1")} XOR {Inp("in2")})";
                case "Inv": return $"NOT({Inp("in")})";  // bitwise NOT
                case "Shl": return $"SHL({Inp("in")}, {Inp("N")})";
                case "Shr": return $"SHR({Inp("in")}, {Inp("N")})";
                case "Rol": return $"ROL({Inp("in")}, {Inp("N")})";
                case "Ror": return $"ROR({Inp("in")}, {Inp("N")})";

                // ── String operations ─────────────────────────────────────────
                case "Concat":  return $"CONCAT({Inp("IN1")}, {Inp("IN2")})";
                case "Left":    return $"LEFT(IN:={Inp("IN")}, L:={Inp("L")})";
                case "Right":   return $"RIGHT(IN:={Inp("IN")}, L:={Inp("L")})";
                case "Mid":     return $"MID(IN:={Inp("IN")}, L:={Inp("L")}, P:={Inp("P")})";
                case "Len":     return $"LEN({Inp("IN")})";
                case "Find":    return $"FIND(IN1:={Inp("IN1")}, IN2:={Inp("IN2")})";
                case "Replace": return $"REPLACE(IN:={Inp("IN")}, IN1:={Inp("IN1")}, L:={Inp("L")}, P:={Inp("P")})";
                case "Insert":  return $"INSERT(IN:={Inp("IN")}, IN1:={Inp("IN1")}, P:={Inp("P")})";
                case "Delete":  return $"DELETE(IN:={Inp("IN")}, L:={Inp("L")}, P:={Inp("P")})";

                // ── RS/SR flip-flops ──────────────────────────────────────────
                case "RS": case "Rs": return $"RS flip-flop: S1:={Inp("S1")}, R:={Inp("R")}";
                case "SR": case "Sr": return $"SR flip-flop: S:={Inp("S")}, R1:={Inp("R1")}";

                // ── Edge detection ────────────────────────────────────────────
                case "PBox": case "RLO_P": return $"P_TRIG({Inp("CLK")})";
                case "NBox": case "RLO_N": return $"N_TRIG({Inp("CLK")})";
                case "FP":  return $"P_TRIG({Inp("in")})";
                case "FN":  return $"N_TRIG({Inp("in")})";

                // ── Timers (with instance name from Part element) ─────────────────────
                case "TON":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "TON(";
                    return $"{prefix}IN:={Inp("IN")}, PT:={Inp("PT")})";
                }
                case "TOF":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "TOF(";
                    return $"{prefix}IN:={Inp("IN")}, PT:={Inp("PT")})";
                }
                case "TP":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "TP(";
                    return $"{prefix}IN:={Inp("IN")}, PT:={Inp("PT")})";
                }
                case "TONR":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "TONR(";
                    return $"{prefix}IN:={Inp("IN")}, R:={Inp("R")}, PT:={Inp("PT")})";
                }

                // ── Counters (with instance name) ────────────────────────────────────
                case "CTU":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "CTU(";
                    return $"{prefix}CU:={Inp("CU")}, R:={Inp("R")}, PV:={Inp("PV")})";
                }
                case "CTD":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "CTD(";
                    return $"{prefix}CD:={Inp("CD")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                }
                case "CTUD":
                {
                    var inst = GetPartInstanceName(part);
                    var prefix = inst != null ? $"{inst}(" : "CTUD(";
                    return $"{prefix}CU:={Inp("CU")}, CD:={Inp("CD")}, R:={Inp("R")}, LD:={Inp("LD")}, PV:={Inp("PV")})";
                }

                // ── Calculate (expression box) ────────────────────────────────────────
                case "Calculate":
                case "CALCULATE":
                {
                    // The expression is stored in a TemplateValue named "Expression"
                    var exprEl = part.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "TemplateValue" &&
                                             e.Attribute("Name")?.Value == "Expression");
                    var expr = exprEl?.Value ?? "?";
                    // Collect all named inputs
                    var calcInputs = wireFrom.Keys
                        .Where(k => k.uid == uid)
                        .OrderBy(k => k.port)
                        .Select(k => { var s = wireFrom[k]; var v = ResolveNode(s.uid, s.port, accessMap, partMap, callMap, wireFrom, depth); return $"{k.port}:={v}"; });
                    return $"CALCULATE({expr}; {string.Join(", ", calcInputs)})";
                }

                // ── Type conversion with explicit type ───────────────────────────────
                case "TypeConvert":
                case "CONV":
                {
                    var tplEl = part.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "TemplateValue" &&
                                             e.Attribute("Name")?.Value == "srcType");
                    var srcType = tplEl?.Value ?? "";
                    var dstEl = part.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "TemplateValue" &&
                                             e.Attribute("Name")?.Value == "dstType");
                    var dstType = dstEl?.Value ?? "";
                    return string.IsNullOrEmpty(srcType)
                        ? $"CONVERT({Inp("in")})"
                        : $"{srcType}_TO_{dstType}({Inp("in")})";
                }

                // ── Generic fallback (unknown Part) ───────────────────────────
                default:
                {
                    // Collect ALL connected inputs dynamically — don't assume cardinality
                    var connectedPorts = wireFrom.Keys
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
                        var src = wireFrom[pk];
                        var v   = ResolveNode(src.uid, src.port, accessMap, partMap, callMap, wireFrom, depth);
                        if (negatedPorts.Contains(pk.port)) v = $"NOT({v})";
                        sb.Append($"{pk.port}:={v}");
                        first = false;
                    }
                    sb.Append(')');
                    return sb.ToString();
                }
            }
        }

        private static List<string> CollectInputsWithNegation(
            string uid, int card,
            HashSet<string> negatedPorts,
            Dictionary<string, XElement> accessMap,
            Dictionary<string, XElement> partMap,
            Dictionary<string, XElement> callMap,
            Dictionary<(string uid, string port), (string uid, string port)> wireFrom,
            int depth)
        {
            var inputs = new List<string>();
            for (int i = 1; i <= card; i++)
            {
                var portName = $"in{i}";
                if (wireFrom.TryGetValue((uid, portName), out var src))
                {
                    var val = ResolveNode(src.uid, src.port, accessMap, partMap, callMap, wireFrom, depth);
                    if (negatedPorts.Contains(portName)) val = $"NOT({val})";
                    inputs.Add(val);
                }
            }
            if (inputs.Count == 0 && wireFrom.TryGetValue((uid, "in"), out var sinSrc))
            {
                var val = ResolveNode(sinSrc.uid, sinSrc.port, accessMap, partMap, callMap, wireFrom, depth);
                if (negatedPorts.Contains("in")) val = $"NOT({val})";
                inputs.Add(val);
            }
            return inputs;
        }

        private static string GetVarName(XElement access)
        {
            var sym = access.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym == null)
                sym = access.Descendants().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (sym == null) return "?";

            var sb = new StringBuilder();
            bool lastWasComponent = false;
            foreach (var el in sym.Elements())
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
                        // In FBD XML, consecutive Components have no "." Token between them —
                        // add it automatically so DB.Member renders as "DB.Member" not "DBMember"
                        if (lastWasComponent && sb.Length > 0)
                            sb.Append('.');
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
                                    // Symbolic index (variable)
                                    var idxSym = idxAcc.Descendants().FirstOrDefault(e2 => e2.Name.LocalName == "Symbol");
                                    return idxSym != null ? GetVarNameFromSymbol(idxSym) : "?";
                                })
                                .ToList();
                            if (indices.Count > 0)
                                sb.Append($"[{string.Join(", ", indices)}]");
                        }
                        lastWasComponent = true;
                        break;
                    }
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

        // ── SCL reconstruction from tokenized XML ────────────────────────────

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
                                    return idxSym != null ? GetVarNameFromSymbol(idxSym) : "?";
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

        // ── Helpers ──────────────────────────────────────────────────────────

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

        // ── Tag Tables ────────────────────────────────────────────────────────

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

        // ── UDTs (PLC Data Types) ─────────────────────────────────────────────

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
