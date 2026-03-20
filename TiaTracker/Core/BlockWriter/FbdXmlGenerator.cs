using System;
using System.Collections.Generic;
using System.Text;

namespace TiaTracker.Core.BlockWriter
{
    /// <summary>
    /// Gera XML compatível com TIA Portal V18 Import para blocos FC/FB em FBD.
    /// Suporta as mesmas instruções que os nossos parsers reconhecem.
    /// </summary>
    public static class FbdXmlGenerator
    {
        // ── Ponto de entrada ─────────────────────────────────────────────────
        public static string Generate(BlockDefinition def)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Document>");
            sb.AppendLine("  <Engineering version=\"V18\"/>");
            sb.AppendLine("  <DocumentInfo>");
            sb.AppendLine($"    <Created>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</Created>");
            sb.AppendLine("    <ExportSetting>WithDefaults</ExportSetting>");
            sb.AppendLine("  </DocumentInfo>");

            var tag = def.BlockType == PlcBlockType.FB ? "SW.Blocks.FB" : "SW.Blocks.FC";
            sb.AppendLine($"  <{tag} ID=\"0\">");
            sb.AppendLine("    <AttributeList>");
            sb.AppendLine("      <AutoNumber>false</AutoNumber>");
            sb.AppendLine("      <HeaderAuthor/>");
            sb.AppendLine("      <HeaderFamily/>");
            sb.AppendLine("      <HeaderName/>");
            sb.AppendLine("      <HeaderVersion>0.1</HeaderVersion>");
            AppendInterface(sb, def);
            sb.AppendLine("      <IsIECCheckEnabled>false</IsIECCheckEnabled>");
            sb.AppendLine("      <MemoryLayout>Optimized</MemoryLayout>");
            sb.AppendLine($"      <Name>{Esc(def.Name)}</Name>");
            sb.AppendLine("      <Namespace/>");
            sb.AppendLine($"      <Number>{def.Number}</Number>");
            sb.AppendLine("      <ProgrammingLanguage>FBD</ProgrammingLanguage>");
            sb.AppendLine("      <SetENOAutomatically>false</SetENOAutomatically>");
            sb.AppendLine("    </AttributeList>");
            sb.AppendLine("    <ObjectList>");

            // Block-level title
            sb.AppendLine("      <MultilingualText ID=\"1\" CompositionName=\"Title\">");
            sb.AppendLine("        <ObjectList>");
            sb.AppendLine("          <MultilingualTextItem ID=\"2\" CompositionName=\"Items\">");
            sb.AppendLine("            <AttributeList>");
            sb.AppendLine("              <Culture>pt-BR</Culture>");
            sb.AppendLine($"              <Text>{Esc(def.Description)}</Text>");
            sb.AppendLine("            </AttributeList>");
            sb.AppendLine("          </MultilingualTextItem>");
            sb.AppendLine("        </ObjectList>");
            sb.AppendLine("      </MultilingualText>");

            // Networks (CompileUnits)
            for (int i = 0; i < def.Networks.Count; i++)
                AppendNetwork(sb, def.Networks[i], i);

            sb.AppendLine("    </ObjectList>");
            sb.AppendLine($"  </{tag}>");
            sb.AppendLine("</Document>");

            return sb.ToString();
        }

        // ── Interface ────────────────────────────────────────────────────────
        static void AppendInterface(StringBuilder sb, BlockDefinition def)
        {
            sb.AppendLine("      <Interface>");
            sb.AppendLine("        <Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">");

            AppendSection(sb, "Input",   def.Inputs);
            AppendSection(sb, "Output",  def.Outputs);
            AppendSection(sb, "InOut",   def.InOuts);

            if (def.BlockType == PlcBlockType.FB)
                AppendSection(sb, "Static", def.Statics);

            AppendSection(sb, "Temp",     def.Temps);
            sb.AppendLine("          <Section Name=\"Constant\"/>");
            sb.AppendLine("          <Section Name=\"Return\">");
            sb.AppendLine("            <Member Name=\"Ret_Val\" Datatype=\"Void\"/>");
            sb.AppendLine("          </Section>");
            sb.AppendLine("        </Sections>");
            sb.AppendLine("      </Interface>");
        }

        static void AppendSection(StringBuilder sb, string name, List<InterfaceVar> vars)
        {
            if (vars == null || vars.Count == 0)
            {
                sb.AppendLine($"          <Section Name=\"{name}\"/>");
                return;
            }
            sb.AppendLine($"          <Section Name=\"{name}\">");
            foreach (var v in vars)
            {
                sb.Append($"            <Member Name=\"{Esc(v.Name)}\" Datatype=\"{Esc(v.DataType)}\"");
                if (!string.IsNullOrWhiteSpace(v.Default))
                    sb.Append($" Remanence=\"NonRetain\"");
                sb.AppendLine(">");
                if (!string.IsNullOrWhiteSpace(v.Comment))
                {
                    sb.AppendLine("              <Comment>");
                    sb.AppendLine($"                <MultiLanguageText Lang=\"pt-BR\">{Esc(v.Comment)}</MultiLanguageText>");
                    sb.AppendLine("              </Comment>");
                }
                if (!string.IsNullOrWhiteSpace(v.Default))
                    sb.AppendLine($"              <StartValue>{Esc(v.Default)}</StartValue>");
                sb.AppendLine("            </Member>");
            }
            sb.AppendLine($"          </Section>");
        }

        // ── CompileUnit (network) ────────────────────────────────────────────
        // Cada rede ocupa 5 IDs hex a partir de 3: CU, Comment, CommentItem, Title, TitleItem
        static void AppendNetwork(StringBuilder sb, NetworkDef net, int index)
        {
            int baseId = 3 + index * 5;
            string cuId  = baseId.ToString("X");
            string cmtId = (baseId + 1).ToString("X");
            string cmiId = (baseId + 2).ToString("X");
            string titId = (baseId + 3).ToString("X");
            string tiiId = (baseId + 4).ToString("X");

            sb.AppendLine($"      <SW.Blocks.CompileUnit ID=\"{cuId}\" CompositionName=\"CompileUnits\">");
            sb.AppendLine("        <AttributeList>");
            sb.AppendLine("          <NetworkSource>");
            sb.AppendLine("            <FlgNet xmlns=\"http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4\">");

            AppendFlgNet(sb, net);

            sb.AppendLine("            </FlgNet>");
            sb.AppendLine("          </NetworkSource>");
            sb.AppendLine("          <ProgrammingLanguage>FBD</ProgrammingLanguage>");
            sb.AppendLine("        </AttributeList>");
            sb.AppendLine("        <ObjectList>");

            // Comment
            sb.AppendLine($"          <MultilingualText ID=\"{cmtId}\" CompositionName=\"Comment\">");
            sb.AppendLine("            <ObjectList>");
            sb.AppendLine($"              <MultilingualTextItem ID=\"{cmiId}\" CompositionName=\"Items\">");
            sb.AppendLine("                <AttributeList>");
            sb.AppendLine("                  <Culture>pt-BR</Culture>");
            sb.AppendLine($"                  <Text>{Esc(net.Comment)}</Text>");
            sb.AppendLine("                </AttributeList>");
            sb.AppendLine($"              </MultilingualTextItem>");
            sb.AppendLine("            </ObjectList>");
            sb.AppendLine($"          </MultilingualText>");

            // Title
            sb.AppendLine($"          <MultilingualText ID=\"{titId}\" CompositionName=\"Title\">");
            sb.AppendLine("            <ObjectList>");
            sb.AppendLine($"              <MultilingualTextItem ID=\"{tiiId}\" CompositionName=\"Items\">");
            sb.AppendLine("                <AttributeList>");
            sb.AppendLine("                  <Culture>pt-BR</Culture>");
            sb.AppendLine($"                  <Text>{Esc(net.Title)}</Text>");
            sb.AppendLine("                </AttributeList>");
            sb.AppendLine($"              </MultilingualTextItem>");
            sb.AppendLine("            </ObjectList>");
            sb.AppendLine($"          </MultilingualText>");

            sb.AppendLine("        </ObjectList>");
            sb.AppendLine($"      </SW.Blocks.CompileUnit>");
        }

        // ── FlgNet (Parts + Wires) ───────────────────────────────────────────
        static void AppendFlgNet(StringBuilder sb, NetworkDef net)
        {
            if (net.Instructions == null || net.Instructions.Count == 0)
            {
                sb.AppendLine("              <Parts/>");
                sb.AppendLine("              <Wires/>");
                return;
            }

            var parts = new List<string>();
            var wires = new List<string>();
            int uid = 21; // UID local da rede, começa em 21 por convenção TIA

            foreach (var inst in net.Instructions)
                BuildInstruction(inst, ref uid, parts, wires);

            sb.AppendLine("              <Parts>");
            foreach (var p in parts) sb.Append(p);
            sb.AppendLine("              </Parts>");
            sb.AppendLine("              <Wires>");
            foreach (var w in wires) sb.Append(w);
            sb.AppendLine("              </Wires>");
        }

        // ── Instrução → Parts + Wires ────────────────────────────────────────
        static void BuildInstruction(InstructionDef inst, ref int uid,
                                     List<string> parts, List<string> wires)
        {
            string type = inst.InstructType ?? "Move";

            switch (type)
            {
                case "Move":         BuildMove(inst, ref uid, parts, wires);      break;
                case "Coil":         BuildCoil(inst, "Coil",  ref uid, parts, wires); break;
                case "SCoil":        BuildCoil(inst, "SCoil", ref uid, parts, wires); break;
                case "RCoil":        BuildCoil(inst, "RCoil", ref uid, parts, wires); break;
                case "Add":          BuildMathN(inst, "Add",  "+", ref uid, parts, wires); break;
                case "Sub":          BuildMath2(inst, "Sub",  ref uid, parts, wires); break;
                case "Mul":          BuildMathN(inst, "Mul",  "*", ref uid, parts, wires); break;
                case "Div":          BuildMath2(inst, "Div",  ref uid, parts, wires); break;
                case "Mod":          BuildMath2(inst, "Mod",  ref uid, parts, wires); break;
                case "Neg":          BuildMath1(inst, "Neg",  ref uid, parts, wires); break;
                case "Abs":          BuildMath1(inst, "Abs",  ref uid, parts, wires); break;
                case "Inc":          BuildInPlace(inst, "Inc", ref uid, parts, wires); break;
                case "Dec":          BuildInPlace(inst, "Dec", ref uid, parts, wires); break;
                case "Eq": case "Ne": case "Gt": case "Lt": case "Ge": case "Le":
                             BuildComparator(inst, ref uid, parts, wires); break;
                case "TON":          BuildTimer(inst, "TON",  ref uid, parts, wires); break;
                case "TOF":          BuildTimer(inst, "TOF",  ref uid, parts, wires); break;
                case "TP":           BuildTimer(inst, "TP",   ref uid, parts, wires); break;
                case "TONR":         BuildTimer(inst, "TONR", ref uid, parts, wires); break;
                case "CTU":          BuildCounter(inst, "CTU", ref uid, parts, wires); break;
                case "CTD":          BuildCounter(inst, "CTD", ref uid, parts, wires); break;
                case "Convert":      BuildConvert(inst, ref uid, parts, wires); break;
                case "AND":          BuildBitGate(inst, "And", ref uid, parts, wires); break;
                case "OR":           BuildBitGate(inst, "Or",  ref uid, parts, wires); break;
                case "MoveBlockI":   BuildMoveBlock(inst, "MoveBlockI", ref uid, parts, wires); break;
                case "MoveBlockU":   BuildMoveBlock(inst, "MoveBlockU", ref uid, parts, wires); break;
                default:             BuildMove(inst, ref uid, parts, wires); break;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // MOVE
        // ────────────────────────────────────────────────────────────────────
        static void BuildMove(InstructionDef inst, ref int uid,
                               List<string> parts, List<string> wires)
        {
            int partUid = uid++;
            int inUid   = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;

            parts.Add(Part("Move", partUid, "                  ",
                $"<TemplateValue Name=\"Card\" Type=\"Cardinality\">1</TemplateValue>"));
            parts.Add(AccessLocal(inUid,  inst.Param1,  "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2,  inUid,  partUid, "in",   "                  "));
            wires.Add(WireOut(wUid3, partUid, "out1", outUid, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // COIL / SCOIL / RCOIL
        // ────────────────────────────────────────────────────────────────────
        static void BuildCoil(InstructionDef inst, string coilName, ref int uid,
                               List<string> parts, List<string> wires)
        {
            // Coil: sinal booleano (Param1) → saída (OutVar1)
            int partUid = uid++;
            int inAcc   = uid++;
            int outAcc  = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;

            parts.Add(Part(coilName, partUid, "                  "));
            parts.Add(AccessLocal(inAcc,  inst.Param1,  "                  "));
            parts.Add(AccessLocal(outAcc, inst.OutVar1, "                  "));

            wires.Add(WireIn(wUid1,  inAcc,  partUid, "in",  "                  "));
            wires.Add(WireOut(wUid2, partUid, "out", outAcc, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // Math N entradas (Add, Mul)
        // ────────────────────────────────────────────────────────────────────
        static void BuildMathN(InstructionDef inst, string name, string op,
                                ref int uid, List<string> parts, List<string> wires)
        {
            // Param1 = in1, Param2 = in2, OutVar1 = out
            int partUid = uid++;
            int in1Uid  = uid++;
            int in2Uid  = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;
            int wUid4   = uid++;

            string srcType = string.IsNullOrWhiteSpace(inst.Param3) ? "Int" : inst.Param3;
            parts.Add(Part(name, partUid, "                  ",
                $"<TemplateValue Name=\"Card\" Type=\"Cardinality\">2</TemplateValue>",
                $"<TemplateValue Name=\"SrcType\" Type=\"Type\">{Esc(srcType)}</TemplateValue>"));
            parts.Add(AccessLocal(in1Uid, inst.Param1, "                  "));
            parts.Add(AccessLocal(in2Uid, inst.Param2, "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2, in1Uid, partUid, "in1", "                  "));
            wires.Add(WireIn(wUid3, in2Uid, partUid, "in2", "                  "));
            wires.Add(WireOut(wUid4, partUid, "out", outUid, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // Math 2 entradas fixas (Sub, Div, Mod)
        // ────────────────────────────────────────────────────────────────────
        static void BuildMath2(InstructionDef inst, string name,
                                ref int uid, List<string> parts, List<string> wires)
            => BuildMathN(inst, name, "-", ref uid, parts, wires); // reutiliza estrutura

        // ────────────────────────────────────────────────────────────────────
        // Math 1 entrada (Neg, Abs)
        // ────────────────────────────────────────────────────────────────────
        static void BuildMath1(InstructionDef inst, string name,
                                ref int uid, List<string> parts, List<string> wires)
        {
            int partUid = uid++;
            int inUid   = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;

            string srcType = string.IsNullOrWhiteSpace(inst.Param3) ? "Int" : inst.Param3;
            parts.Add(Part(name, partUid, "                  ",
                $"<TemplateValue Name=\"SrcType\" Type=\"Type\">{Esc(srcType)}</TemplateValue>"));
            parts.Add(AccessLocal(inUid,  inst.Param1,  "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2,  inUid,  partUid, "in",  "                  "));
            wires.Add(WireOut(wUid3, partUid, "out", outUid, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // Inc / Dec (in-place, sem saída separada)
        // ────────────────────────────────────────────────────────────────────
        static void BuildInPlace(InstructionDef inst, string name,
                                  ref int uid, List<string> parts, List<string> wires)
        {
            int partUid  = uid++;
            int opUid    = uid++;
            int enUid    = uid++;
            int wUid1    = uid++;
            int wUid2    = uid++;

            string dataType = string.IsNullOrWhiteSpace(inst.Param3) ? "Int" : inst.Param3;
            parts.Add(Part(name, partUid, "                  ",
                $"<TemplateValue Name=\"DestType\" Type=\"Type\">{Esc(dataType)}</TemplateValue>"));
            parts.Add(AccessLocal(opUid, inst.Param1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2, opUid, partUid, "operand", "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // Comparators (Eq/Ne/Gt/Lt/Ge/Le)
        // ────────────────────────────────────────────────────────────────────
        static void BuildComparator(InstructionDef inst,
                                     ref int uid, List<string> parts, List<string> wires)
        {
            int partUid = uid++;
            int in1Uid  = uid++;
            int in2Uid  = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;
            int wUid4   = uid++;

            string srcType = string.IsNullOrWhiteSpace(inst.Param3) ? "Int" : inst.Param3;
            parts.Add(Part(inst.InstructType, partUid, "                  ",
                $"<TemplateValue Name=\"SrcType\" Type=\"Type\">{Esc(srcType)}</TemplateValue>"));
            parts.Add(AccessLocal(in1Uid, inst.Param1,  "                  "));
            parts.Add(AccessLocal(in2Uid, inst.Param2,  "                  "));

            // Saída do comparador → Coil (OutVar1) ou outra instrução
            if (!string.IsNullOrWhiteSpace(inst.OutVar1))
            {
                // resultado → Coil
                int coilUid   = uid++;
                int coilAcc   = uid++;
                int wCoil1    = uid++;
                int wCoil2    = uid++;

                parts.Add(Part("Coil", coilUid, "                  "));
                parts.Add(AccessLocal(coilAcc, inst.OutVar1, "                  "));

                wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
                wires.Add(WireIn(wUid2, in1Uid, partUid, "in1", "                  "));
                wires.Add(WireIn(wUid3, in2Uid, partUid, "in2", "                  "));
                // out do comparador → in do coil
                wires.Add(Part2Part(wUid4, partUid, "out", coilUid, "in", "                  "));
                wires.Add(WireOut(wCoil1, coilUid, "out", coilAcc, "                  "));
            }
            else
            {
                // sem destino explícito — só gera a parte
                wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
                wires.Add(WireIn(wUid2, in1Uid, partUid, "in1", "                  "));
                wires.Add(WireIn(wUid3, in2Uid, partUid, "in2", "                  "));
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Timers: TON / TOF / TP / TONR
        // ────────────────────────────────────────────────────────────────────
        static void BuildTimer(InstructionDef inst, string timerType,
                                ref int uid, List<string> parts, List<string> wires)
        {
            // Param1 = IN signal, Param2 = PT, Param3 = DataType (default "IEC_TIMER")
            // OutVar1 = variável Q (→ Coil), OutVar2 = variável ET (→ IdentCon)
            // inst.EnableSignal = nome da instância DB (ou será gerado auto)
            string instName = string.IsNullOrWhiteSpace(inst.EnableSignal)
                              ? $"{inst.Param1}_{timerType}"
                              : inst.EnableSignal;
            string dataType = string.IsNullOrWhiteSpace(inst.Param3) ? "IEC_TIMER" : inst.Param3;

            int partUid = uid++;
            int instUid = uid++;   // Access para instância
            int inUid   = uid++;
            int ptUid   = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;

            // Part com Instance
            var sb = new StringBuilder();
            sb.AppendLine($"                <Part Name=\"{timerType}\" Version=\"1.0\" UId=\"{partUid}\">");
            sb.AppendLine($"                  <Instance Scope=\"GlobalVariable\" UId=\"{instUid}\">");
            sb.AppendLine($"                    <Component Name=\"{Esc(instName)}\"/>");
            sb.AppendLine("                  </Instance>");
            sb.AppendLine($"                  <TemplateValue Name=\"InstanceOperand\" Type=\"Type\">{Esc(dataType)}</TemplateValue>");
            sb.AppendLine("                </Part>");
            parts.Add(sb.ToString());

            parts.Add(AccessLocal(inUid, inst.Param1, "                  "));
            if (!string.IsNullOrWhiteSpace(inst.Param2))
                parts.Add(AccessLocal(ptUid, inst.Param2, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, "", "                  ")); // timer sem enable externo
            wires.Add(WireIn(wUid2, inUid, partUid, "IN", "                  "));
            if (!string.IsNullOrWhiteSpace(inst.Param2))
                wires.Add(WireIn(wUid3, ptUid, partUid, "PT", "                  "));

            // Q → Coil se OutVar1 definido
            if (!string.IsNullOrWhiteSpace(inst.OutVar1))
            {
                int coilUid = uid++;
                int coilAcc = uid++;
                int wC1     = uid++;
                int wC2     = uid++;
                parts.Add(Part("Coil", coilUid, "                  "));
                parts.Add(AccessLocal(coilAcc, inst.OutVar1, "                  "));
                wires.Add(Part2Part(wC1, partUid, "Q", coilUid, "in", "                  "));
                wires.Add(WireOut(wC2, coilUid, "out", coilAcc, "                  "));
            }

            // ET → IdentCon se OutVar2 definido
            if (!string.IsNullOrWhiteSpace(inst.OutVar2))
            {
                int etAcc = uid++;
                int wET   = uid++;
                parts.Add(AccessLocal(etAcc, inst.OutVar2, "                  "));
                wires.Add(WireOut(wET, partUid, "ET", etAcc, "                  "));
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Counters: CTU / CTD
        // ────────────────────────────────────────────────────────────────────
        static void BuildCounter(InstructionDef inst, string ctrType,
                                  ref int uid, List<string> parts, List<string> wires)
        {
            // Param1 = CU/CD signal, Param2 = PV, Param3 = DataType (default "IEC_UCOUNTER")
            // EnableSignal = instance name
            // OutVar1 = Q → Coil, OutVar2 = CV → IdentCon
            string instName = string.IsNullOrWhiteSpace(inst.EnableSignal)
                              ? $"{ctrType}_DB"
                              : inst.EnableSignal;
            string dataType = string.IsNullOrWhiteSpace(inst.Param3) ? "IEC_UCOUNTER" : inst.Param3;

            int partUid = uid++;
            int instUid = uid++;
            int cuUid   = uid++;
            int pvUid   = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;

            string cuPort = ctrType == "CTD" ? "CD" : "CU";

            var sb = new StringBuilder();
            sb.AppendLine($"                <Part Name=\"{ctrType}\" Version=\"1.0\" UId=\"{partUid}\">");
            sb.AppendLine($"                  <Instance Scope=\"GlobalVariable\" UId=\"{instUid}\">");
            sb.AppendLine($"                    <Component Name=\"{Esc(instName)}\"/>");
            sb.AppendLine("                  </Instance>");
            sb.AppendLine($"                  <TemplateValue Name=\"value_type\" Type=\"Type\">{Esc(dataType)}</TemplateValue>");
            sb.AppendLine("                </Part>");
            parts.Add(sb.ToString());

            parts.Add(AccessLocal(cuUid, inst.Param1, "                  "));
            if (!string.IsNullOrWhiteSpace(inst.Param2))
                parts.Add(AccessLocal(pvUid, inst.Param2, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, "", "                  "));
            wires.Add(WireIn(wUid2, cuUid, partUid, cuPort, "                  "));
            if (!string.IsNullOrWhiteSpace(inst.Param2))
                wires.Add(WireIn(wUid3, pvUid, partUid, "PV", "                  "));

            // Q → Coil
            if (!string.IsNullOrWhiteSpace(inst.OutVar1))
            {
                int coilUid = uid++;
                int coilAcc = uid++;
                int wC1     = uid++;
                int wC2     = uid++;
                parts.Add(Part("Coil", coilUid, "                  "));
                parts.Add(AccessLocal(coilAcc, inst.OutVar1, "                  "));
                wires.Add(Part2Part(wC1, partUid, "Q", coilUid, "in", "                  "));
                wires.Add(WireOut(wC2, coilUid, "out", coilAcc, "                  "));
            }

            // CV → IdentCon
            if (!string.IsNullOrWhiteSpace(inst.OutVar2))
            {
                int cvAcc = uid++;
                int wCV   = uid++;
                parts.Add(AccessLocal(cvAcc, inst.OutVar2, "                  "));
                wires.Add(WireOut(wCV, partUid, "CV", cvAcc, "                  "));
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Convert
        // ────────────────────────────────────────────────────────────────────
        static void BuildConvert(InstructionDef inst,
                                  ref int uid, List<string> parts, List<string> wires)
        {
            int partUid = uid++;
            int inUid   = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;

            string srcType  = string.IsNullOrWhiteSpace(inst.Param3) ? "Int"  : inst.Param3;
            string destType = string.IsNullOrWhiteSpace(inst.Param4) ? "Real" : inst.Param4;

            parts.Add(Part("Convert", partUid, "                  ",
                $"<TemplateValue Name=\"SrcType\"  Type=\"Type\">{Esc(srcType)}</TemplateValue>",
                $"<TemplateValue Name=\"DestType\" Type=\"Type\">{Esc(destType)}</TemplateValue>"));
            parts.Add(AccessLocal(inUid,  inst.Param1,  "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2,  inUid,  partUid, "in",  "                  "));
            wires.Add(WireOut(wUid3, partUid, "out", outUid, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // Bit Gate (AND / OR)
        // ────────────────────────────────────────────────────────────────────
        static void BuildBitGate(InstructionDef inst, string gateName,
                                  ref int uid, List<string> parts, List<string> wires)
        {
            int partUid = uid++;
            int in1Uid  = uid++;
            int in2Uid  = uid++;
            int outUid  = uid++;
            int enUid   = uid++;
            int wUid1   = uid++;
            int wUid2   = uid++;
            int wUid3   = uid++;
            int wUid4   = uid++;

            parts.Add(Part(gateName, partUid, "                  ",
                "<TemplateValue Name=\"Card\" Type=\"Cardinality\">2</TemplateValue>"));
            parts.Add(AccessLocal(in1Uid, inst.Param1,  "                  "));
            parts.Add(AccessLocal(in2Uid, inst.Param2,  "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2, in1Uid, partUid, "in1", "                  "));
            wires.Add(WireIn(wUid3, in2Uid, partUid, "in2", "                  "));
            wires.Add(WireOut(wUid4, partUid, "out", outUid, "                  "));
        }

        // ────────────────────────────────────────────────────────────────────
        // MoveBlockI / MoveBlockU
        // ────────────────────────────────────────────────────────────────────
        static void BuildMoveBlock(InstructionDef inst, string name,
                                    ref int uid, List<string> parts, List<string> wires)
        {
            int partUid  = uid++;
            int inUid    = uid++;
            int cntUid   = uid++;
            int outUid   = uid++;
            int enUid    = uid++;
            int wUid1    = uid++;
            int wUid2    = uid++;
            int wUid3    = uid++;
            int wUid4    = uid++;

            parts.Add(Part(name, partUid, "                  ",
                "<TemplateValue Name=\"Card\" Type=\"Cardinality\">1</TemplateValue>"));
            parts.Add(AccessLocal(inUid,  inst.Param1,  "                  "));
            parts.Add(AccessLocal(cntUid, inst.Param2,  "                  "));
            parts.Add(AccessLocal(outUid, inst.OutVar1, "                  "));

            wires.Add(WireEnPwr(wUid1, partUid, enUid, inst.EnableSignal, "                  "));
            wires.Add(WireIn(wUid2, inUid,  partUid, "in",    "                  "));
            wires.Add(WireIn(wUid3, cntUid, partUid, "count", "                  "));
            wires.Add(WireOut(wUid4, partUid, "out", outUid,  "                  "));
        }

        // ── Primitivas XML ───────────────────────────────────────────────────

        static string Part(string name, int partUid, string indent, params string[] children)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Part Name=\"{name}\" UId=\"{partUid}\">");
            foreach (var c in children)
                sb.AppendLine($"{indent}  {c}");
            sb.AppendLine($"{indent}</Part>");
            return sb.ToString();
        }

        static string AccessLocal(int accUid, string varName, string indent)
        {
            if (string.IsNullOrWhiteSpace(varName)) varName = "?";
            // Suporte a path com ponto: DB.Campo → dois Component
            var parts = varName.Split('.');
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Access Scope=\"LocalVariable\" UId=\"{accUid}\">");
            sb.AppendLine($"{indent}  <Symbol>");
            foreach (var p in parts)
                sb.AppendLine($"{indent}    <Component Name=\"{Esc(p)}\"/>");
            sb.AppendLine($"{indent}  </Symbol>");
            sb.AppendLine($"{indent}</Access>");
            return sb.ToString();
        }

        // Wire: Powerrail/IdentCon → NameCon(partUid, "en")
        static string WireEnPwr(int wireUid, int partUid, int openUid,
                                 string enableSignal, string indent)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Wire UId=\"{wireUid}\">");
            if (string.IsNullOrWhiteSpace(enableSignal))
            {
                sb.AppendLine($"{indent}  <Powerrail/>");
            }
            else
            {
                sb.AppendLine($"{indent}  <IdentCon UId=\"{openUid}\"/>"); // placeholder
            }
            sb.AppendLine($"{indent}  <NameCon UId=\"{partUid}\" Name=\"en\"/>");
            sb.AppendLine($"{indent}</Wire>");
            return sb.ToString();
        }

        // Wire: IdentCon(accUid) → NameCon(partUid, portName)
        static string WireIn(int wireUid, int accUid, int partUid, string port, string indent)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Wire UId=\"{wireUid}\">");
            sb.AppendLine($"{indent}  <IdentCon UId=\"{accUid}\"/>");
            sb.AppendLine($"{indent}  <NameCon UId=\"{partUid}\" Name=\"{port}\"/>");
            sb.AppendLine($"{indent}</Wire>");
            return sb.ToString();
        }

        // Wire: NameCon(partUid, portName) → IdentCon(accUid)
        static string WireOut(int wireUid, int partUid, string port, int accUid, string indent)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Wire UId=\"{wireUid}\">");
            sb.AppendLine($"{indent}  <NameCon UId=\"{partUid}\" Name=\"{port}\"/>");
            sb.AppendLine($"{indent}  <IdentCon UId=\"{accUid}\"/>");
            sb.AppendLine($"{indent}</Wire>");
            return sb.ToString();
        }

        // Wire: NameCon(src) → NameCon(dst) — part to part
        static string Part2Part(int wireUid, int srcPartUid, string srcPort,
                                 int dstPartUid, string dstPort, string indent)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}<Wire UId=\"{wireUid}\">");
            sb.AppendLine($"{indent}  <NameCon UId=\"{srcPartUid}\" Name=\"{srcPort}\"/>");
            sb.AppendLine($"{indent}  <NameCon UId=\"{dstPartUid}\" Name=\"{dstPort}\"/>");
            sb.AppendLine($"{indent}</Wire>");
            return sb.ToString();
        }

        static string Esc(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
