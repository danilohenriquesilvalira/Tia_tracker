using System;
using System.IO;
using System.Linq;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace TiaTracker.Core.BlockWriter
{
    /// <summary>
    /// Importa um bloco (XML gerado pelo FbdXmlGenerator) no TIA Portal via Openness API.
    /// Também pode adicionar a chamada do bloco no OB1.
    /// </summary>
    public class BlockImporter
    {
        private readonly TiaConnection _conn;

        public BlockImporter(TiaConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        // ── Importar XML de bloco ────────────────────────────────────────────
        /// <summary>
        /// Importa o XML no primeiro dispositivo PLC do projecto.
        /// Retorna o nome do bloco importado.
        /// </summary>
        public string ImportBlock(string xml, string deviceName = null)
        {
            var software = GetPlcSoftware(deviceName);
            if (software == null)
                throw new InvalidOperationException("Nenhum PLC software encontrado no projecto.");

            var tempFile = Path.Combine(Path.GetTempPath(), $"TiaTracker_Import_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempFile, xml, Encoding.UTF8);
                Console.WriteLine($"  A importar bloco...");
                software.BlockGroup.Blocks.Import(new FileInfo(tempFile), ImportOptions.Override);
                Console.WriteLine("  Bloco importado com sucesso!");
                return ExtractBlockName(xml);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        // ── Adicionar chamada no OB1 ─────────────────────────────────────────
        /// <summary>
        /// Adiciona uma nova rede no OB1 que chama o bloco importado.
        /// </summary>
        public void AddCallToOb1(string blockName, string blockType, string deviceName = null)
        {
            var software = GetPlcSoftware(deviceName);
            if (software == null) return;

            // Encontrar o OB1
            var ob1 = software.BlockGroup.Blocks
                .OfType<OB>()
                .FirstOrDefault(b => b.Name == "Main" || b.Number == 1);

            if (ob1 == null)
            {
                Console.WriteLine("  OB1 (Main) não encontrado — chamada não adicionada.");
                return;
            }

            // Exportar OB1 actual
            var tempExport = Path.Combine(Path.GetTempPath(), $"TiaTracker_OB1_Export_{Guid.NewGuid():N}.xml");
            var tempImport = Path.Combine(Path.GetTempPath(), $"TiaTracker_OB1_Import_{Guid.NewGuid():N}.xml");
            try
            {
                ob1.Export(new FileInfo(tempExport), ExportOptions.WithDefaults);
                var ob1Xml = File.ReadAllText(tempExport, Encoding.UTF8);

                // Injectar nova rede com CALL
                var updatedXml = InjectCallNetwork(ob1Xml, blockName, blockType);
                if (updatedXml == null)
                {
                    Console.WriteLine("  Não foi possível injectar chamada no OB1.");
                    return;
                }

                File.WriteAllText(tempImport, updatedXml, Encoding.UTF8);
                software.BlockGroup.Blocks.Import(new FileInfo(tempImport), ImportOptions.Override);
                Console.WriteLine($"  Chamada de {blockName} adicionada ao OB1!");
            }
            finally
            {
                try { File.Delete(tempExport); } catch { }
                try { File.Delete(tempImport); } catch { }
            }
        }

        // ── Injectar rede de CALL no OB1 XML ─────────────────────────────────
        private static string InjectCallNetwork(string ob1Xml, string blockName, string blockType)
        {
            // Encontrar o último ID hex usado no XML
            int maxId = FindMaxHexId(ob1Xml);
            int nextId = maxId + 1;

            // Nova rede com CALL
            string cuId  = nextId.ToString("X");
            string cmtId = (nextId + 1).ToString("X");
            string cmiId = (nextId + 2).ToString("X");
            string titId = (nextId + 3).ToString("X");
            string tiiId = (nextId + 4).ToString("X");
            string callId = "21";
            string openId = "22";
            string wireId = "23";

            var newNetwork = $@"
      <SW.Blocks.CompileUnit ID=""{cuId}"" CompositionName=""CompileUnits"">
        <AttributeList>
          <NetworkSource>
            <FlgNet xmlns=""http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4"">
              <Parts>
                <Call UId=""{callId}"">
                  <CallInfo Name=""{EscXml(blockName)}"" BlockType=""{blockType}""/>
                </Call>
              </Parts>
              <Wires>
                <Wire UId=""{wireId}"">
                  <OpenCon UId=""{openId}""/>
                  <NameCon UId=""{callId}"" Name=""en""/>
                </Wire>
              </Wires>
            </FlgNet>
          </NetworkSource>
          <ProgrammingLanguage>FBD</ProgrammingLanguage>
        </AttributeList>
        <ObjectList>
          <MultilingualText ID=""{cmtId}"" CompositionName=""Comment"">
            <ObjectList>
              <MultilingualTextItem ID=""{cmiId}"" CompositionName=""Items"">
                <AttributeList>
                  <Culture>pt-BR</Culture>
                  <Text/>
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
          <MultilingualText ID=""{titId}"" CompositionName=""Title"">
            <ObjectList>
              <MultilingualTextItem ID=""{tiiId}"" CompositionName=""Items"">
                <AttributeList>
                  <Culture>pt-BR</Culture>
                  <Text>CALL {EscXml(blockName)}</Text>
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
        </ObjectList>
      </SW.Blocks.CompileUnit>";

            // Inserir antes do fecho </ObjectList> do bloco
            int insertPos = ob1Xml.LastIndexOf("</ObjectList>", StringComparison.Ordinal);
            if (insertPos < 0) return null;

            return ob1Xml.Substring(0, insertPos) + newNetwork + "\n" + ob1Xml.Substring(insertPos);
        }

        // ── Utilitários ───────────────────────────────────────────────────────
        private PlcSoftware GetPlcSoftware(string deviceName)
        {
            if (_conn?.Project == null) return null;

            foreach (var device in _conn.Project.Devices)
            {
                if (deviceName != null &&
                    !device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var item in device.DeviceItems)
                {
                    var svc = item.GetService<SoftwareContainer>();
                    if (svc?.Software is PlcSoftware plc)
                        return plc;
                }
            }
            return null;
        }

        private static int FindMaxHexId(string xml)
        {
            int max = 0;
            int pos = 0;
            while (true)
            {
                int idx = xml.IndexOf(" ID=\"", pos, StringComparison.Ordinal);
                if (idx < 0) break;
                int start = idx + 5;
                int end   = xml.IndexOf('"', start);
                if (end < 0) break;
                var hexStr = xml.Substring(start, end - start);
                if (int.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out int val))
                    if (val > max) max = val;
                pos = end + 1;
            }
            return max;
        }

        private static string ExtractBlockName(string xml)
        {
            var nameTag = "<Name>";
            int idx = xml.IndexOf(nameTag, StringComparison.Ordinal);
            if (idx < 0) return "?";
            int start = idx + nameTag.Length;
            int end   = xml.IndexOf("</Name>", start, StringComparison.Ordinal);
            return end < 0 ? "?" : xml.Substring(start, end - start);
        }

        private static string EscXml(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
