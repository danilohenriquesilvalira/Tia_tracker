using System;

namespace TiaTracker.Core.BlockWriter
{
    /// <summary>
    /// Gera XML TIA Portal V18 para blocos SCL (FC, FB, DB de instância, DB global).
    /// O código SCL é passado directamente como texto — sem parsing, sem transformação.
    /// </summary>
    public static class SclBlockXmlGenerator
    {
        // ── FC em SCL ────────────────────────────────────────────────────────
        public static string GenerateFC(string name, int number, string sclCode)
        {
            return BlockXml("SW.Blocks.FC", name, number, "SCL", sclCode);
        }

        // ── FB em SCL ────────────────────────────────────────────────────────
        public static string GenerateFB(string name, int number, string sclCode)
        {
            return BlockXml("SW.Blocks.FB", name, number, "SCL", sclCode);
        }

        // ── DB de Instância ──────────────────────────────────────────────────
        public static string GenerateInstanceDB(string dbName, int dbNumber,
                                                 string fbName, int fbNumber)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V18""/>
  <DocumentInfo>
    <Created>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</Created>
    <ExportSetting>WithDefaults</ExportSetting>
  </DocumentInfo>
  <SW.Blocks.InstanceDB ID=""0"">
    <AttributeList>
      <AutoNumber>false</AutoNumber>
      <InstanceOfName>{Esc(fbName)}</InstanceOfName>
      <InstanceOfNumber>{fbNumber}</InstanceOfNumber>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>{Esc(dbName)}</Name>
      <Number>{dbNumber}</Number>
      <ProgrammingLanguage>DB</ProgrammingLanguage>
    </AttributeList>
  </SW.Blocks.InstanceDB>
</Document>";
        }

        // ── DB Global ────────────────────────────────────────────────────────
        public static string GenerateGlobalDB(string dbName, int dbNumber)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V18""/>
  <DocumentInfo>
    <Created>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</Created>
    <ExportSetting>WithDefaults</ExportSetting>
  </DocumentInfo>
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <AutoNumber>false</AutoNumber>
      <HeaderAuthor/>
      <HeaderFamily/>
      <HeaderName/>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface>
        <Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5"">
          <Section Name=""Static""/>
        </Sections>
      </Interface>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>{Esc(dbName)}</Name>
      <Number>{dbNumber}</Number>
      <ProgrammingLanguage>DB</ProgrammingLanguage>
    </AttributeList>
  </SW.Blocks.GlobalDB>
</Document>";
        }

        // ── Bloco SCL genérico (FC ou FB) ─────────────────────────────────────
        private static string BlockXml(string tag, string name, int number,
                                        string lang, string sclCode)
        {
            // O código SCL vai dentro de <StructuredText> no CompileUnit
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V18""/>
  <DocumentInfo>
    <Created>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</Created>
    <ExportSetting>WithDefaults</ExportSetting>
  </DocumentInfo>
  <{tag} ID=""0"">
    <AttributeList>
      <AutoNumber>false</AutoNumber>
      <HeaderAuthor/>
      <HeaderFamily/>
      <HeaderName/>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface>
        <Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5"">
          <Section Name=""Input""/>
          <Section Name=""Output""/>
          <Section Name=""InOut""/>
          <Section Name=""Temp""/>
          <Section Name=""Constant""/>
          <Section Name=""Return"">
            <Member Name=""Ret_Val"" Datatype=""Void""/>
          </Section>
        </Sections>
      </Interface>
      <IsIECCheckEnabled>false</IsIECCheckEnabled>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>{Esc(name)}</Name>
      <Namespace/>
      <Number>{number}</Number>
      <ProgrammingLanguage>{lang}</ProgrammingLanguage>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Title"">
        <ObjectList>
          <MultilingualTextItem ID=""2"" CompositionName=""Items"">
            <AttributeList>
              <Culture>pt-BR</Culture>
              <Text/>
            </AttributeList>
          </MultilingualTextItem>
        </ObjectList>
      </MultilingualText>
      <SW.Blocks.CompileUnit ID=""3"" CompositionName=""CompileUnits"">
        <AttributeList>
          <NetworkSource>
            <StructuredText>{EscCode(sclCode)}</StructuredText>
          </NetworkSource>
          <ProgrammingLanguage>SCL</ProgrammingLanguage>
        </AttributeList>
        <ObjectList>
          <MultilingualText ID=""4"" CompositionName=""Comment"">
            <ObjectList>
              <MultilingualTextItem ID=""5"" CompositionName=""Items"">
                <AttributeList>
                  <Culture>pt-BR</Culture>
                  <Text/>
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
          <MultilingualText ID=""6"" CompositionName=""Title"">
            <ObjectList>
              <MultilingualTextItem ID=""7"" CompositionName=""Items"">
                <AttributeList>
                  <Culture>pt-BR</Culture>
                  <Text/>
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
        </ObjectList>
      </SW.Blocks.CompileUnit>
    </ObjectList>
  </{tag}>
</Document>";
        }

        static string Esc(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // Para código SCL dentro de CDATA-like — preserva tabulações e quebras
        static string EscCode(string s) => Esc(s ?? "");
    }
}
