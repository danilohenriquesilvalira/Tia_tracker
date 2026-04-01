---
description: Adiciona ou corrige um parser FBD/LAD no TiaTracker. Uso: /parser [instrução ou problema]
---

Tarefa: trabalhar nos parsers FBD/LAD do TiaTracker.

**Arquitetura dos parsers:**
- `Core/FbdParsers/FbdContext.cs` — central: método `Dispatch()` roteia cada Part para o parser certo, `ResolveNode()` resolve recursivamente wires
- Cada parser recebe `FbdContext ctx` e `XElement part` e devolve `string` (expressão legível)
- `GetVarName()` / `GetVarNameFromSymbol()` convertem Access elements em nomes/endereços
- Profundidade máxima de recursão: 12 níveis (evita loops infinitos)

**Parsers existentes:**
- `BitLogicParser.cs` — AND, OR, XOR, NOT, Contact, Coil, SR/RS, R_TRIG, F_TRIG
- `TimerParser.cs` — S, SD, SS, TP (resolve PT, IN, Q)
- `CounterParser.cs` — C, CU, CD (resolve PV, CV)
- `ComparatorParser.cs` — ==, <>, <, >, <=, >=
- `MathParser.cs` — +, -, *, /, MOD, ABS, MIN, MAX, SQRT
- `MoveParser.cs` — MOVE, MOVE_B, SWAP, INSERT, EXTRACT
- `ConversionParser.cs` — conversões de tipo (INT→DINT, BCD, HEX)
- `WordLogicParser.cs` — operações bit a bit, shift, rotate
- `StringParser.cs` — concatenação, comparação de strings
- `ProgramControlParser.cs` — CALL, RET, STOP, HALT

**Para adicionar novo parser:**
1. Criar `Core/FbdParsers/NovoParser.cs` seguindo o padrão dos existentes
2. Registar o caso no método `Dispatch()` em `FbdContext.cs`
3. Testar com XML de rede real exportado pelo TIA Portal

Instrução ou problema: $ARGUMENTS
