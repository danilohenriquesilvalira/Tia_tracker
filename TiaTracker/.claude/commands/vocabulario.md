---
description: Resolver o gap vocabulário operador vs tags PLC no RAG. Uso: /vocabulario [problema ou tag]
---

## Gap de Vocabulário: Operador vs PLC

**Problema central:** Os operadores não conhecem os nomes das tags PLC.
- Operador diz: *"o pistão de enchimento não sobe"*
- Tag PLC é: `FC_SUBIDA_AB_COMP_A` ou `OS_PISTAO_ENCH_A`
- Se o embedding não encontrar a palavra "pistão" no ficheiro → retrieval falha

---

## Solução: Prose rico em linguagem operacional

Cada ficheiro de tags gerado pelo TiaTracker começa com um parágrafo de 3–5 frases usando vocabulário de operador:

**Exemplo para `di_fins_curso.md`:**
> "Este grupo contém os sensores de posição (fins de curso) que confirmam a posição física dos atuadores. Incluem confirmações de abertura e fecho de comportas, válvulas e pistões. Quando um cilindro ou pistão chega ao fim do curso, o sensor envia sinal ao PLC para confirmar a posição. Estes sinais são essenciais para a sequência automática de enchimento e esvaziamento da eclusa."

**Exemplo para `mb_defeitos.md`:**
> "Este grupo contém os bits de defeito (DEF_) que o PLC ativa quando deteta uma avaria. Cada bit corresponde a um equipamento: bomba, comporta, pistão, válvula ou motor. Quando um defeito está ativo, o equipamento fica bloqueado até o operador reconhecer e resolver a avaria. Estes bits são normalmente a causa de alarmes visíveis no SCADA ou painel HMI."

---

## `InferTagMeaning` — Dicionário de tokens PLC → Português

Função em `MainForm.cs` que converte automaticamente nomes de tags em linguagem natural.

**Tokens mapeados (exemplos):**
| Token | Significado |
|-------|-------------|
| `DEF` | Defeito |
| `AL` | Alarme |
| `PROT` | Proteção |
| `BOMB` | Bomba |
| `COMP` | Comporta |
| `VALV` | Válvula |
| `PIST` | Pistão |
| `ENCOD` | Encoder |
| `FC` | Fim de Curso |
| `RM` | Retorno de Marcha |
| `OS` | Ordem de Saída |
| `OM` | Ordem de Marcha |
| `SIN` | Sinalização |
| `INTERF` | Interface |
| `INTERD` | Interdição |
| `AUTOR` | Autorização |
| `EMERG` | Emergência |
| `COND` | Condição |
| `REG` | Registo |
| `IMP` | Impulso |
| `AB` | Abertura |
| `FECH` | Fecho |
| `SUBIDA` | Subida |
| `DESCIDA` | Descida |
| `ENT` | Entrada (estado) |
| `SAID` | Saída (estado) |
| `ENCH` | Enchimento |
| `ESVAZ` | Esvaziamento |
| `JUS` | Jusante |
| `MON` | Montante |
| `A`, `B`, `C` | (sufixo de equipamento A/B/C) |

**Resultado:** `DEF_BOMB_A` → *"Defeito — Bomba A"*

---

## `HumanAddress` — Endereços embedding-friendly

O símbolo `%` não tem valor semântico nos modelos de embedding. Converter:

| Formato PLC | Formato RAG |
|-------------|-------------|
| `%I3.2` | `Entrada I3.2` |
| `%Q2.1` | `Saída Q2.1` |
| `%M10.0` | `Memória M10.0` |
| `%IW4` | `Entrada Word IW4` |
| `%QW6` | `Saída Word QW6` |
| `%MW12` | `Memória Word MW12` |

---

## Extensão do dicionário

Para adicionar novos tokens ao `InferTagMeaning`, editar o dicionário em `MainForm.cs`:
```csharp
var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "NOVO_TOKEN", "Tradução PT" },
    // ...
};
```

**Quando adicionar:**
- Quando aparece um prefixo novo num projeto PLC diferente
- Quando o operador usa uma palavra que não está mapeada
- Quando o "Significado" na tabela exportada mostra o token raw (ex: "HIDR" em vez de "Hidráulico")

---

**Tarefa atual:** $ARGUMENTS
