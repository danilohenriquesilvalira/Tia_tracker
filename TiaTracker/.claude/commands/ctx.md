---
description: Contexto completo do projeto TiaTracker para não reexplicar a stack
---

**TiaTracker** — aplicação C# Windows Forms que conecta ao TIA Portal V18, lê projetos PLC (.ap18) e gera markdown otimizado para RAG/LLM.

**Stack:** C# (.NET Framework 4.8 + net10.0), WinForms, Siemens.Engineering V18 API, Newtonsoft.Json. Plataforma x64 Windows only.

**Estrutura principal:**
- `Core/` — lógica de negócio
  - `ProjectReader.cs` — lê todos os blocos do projeto TIA
  - `BlockExporter.cs` — exporta blocos para XML
  - `TiaConnection.cs` — liga ao processo TIA Portal
  - `HardwareReader.cs` — lê topologia de hardware (CPUs, módulos, I/O)
  - `FbdParsers/` — parsers especializados por tipo de instrução FBD/LAD
    - `FbdContext.cs` — coração do parser: resolve wires, despacha para parsers
    - `BitLogicParser.cs` — AND, OR, NOT, contatos, bobinas, flip-flops
    - `TimerParser.cs` — S, SD, SS, TP
    - `CounterParser.cs` — C, CU, CD
    - `ComparatorParser.cs`, `MathParser.cs`, `MoveParser.cs`, etc.
  - `TcpServer/` — servidor TCP opcional para dados live do PLC
- `UI/MainForm.cs` (2645 linhas) — GUI + geração de markdown + export XML
- `Program.cs` — entry point

**O que gera:**
- Markdown AI-optimizado: hardware, tag tables, UDTs, blocos (OB/FB/FC/DB), grafo de chamadas, índice de falhas, cross-reference de tags, I/O livres
- XML estruturado com toda a informação do projeto
- Filename output: `DaniloTracker_IA_<ProjectName>_<YYYYMMDD_HHmmss>.md`

**Projeto alvo atual:** Barragem de Crestuma-Lever (múltiplos PLCs)

$ARGUMENTS
