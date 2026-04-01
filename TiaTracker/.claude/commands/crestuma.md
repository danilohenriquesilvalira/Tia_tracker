---
description: Contexto dos PLCs da Barragem de Crestuma-Lever e estrutura de ficheiros RAG. Uso: /crestuma [tarefa]
---

**Projeto:** Barragem de Crestuma-Lever — múltiplos PLCs Siemens S7

**PLCs existentes:**
| PLC | Função |
|-----|--------|
| `PLC_Comando` | Supervisão e comando geral da eclusa |
| `PLC_Enchimento` | Controlo do processo de enchimento da câmara |
| `PLC_Esvaziamento` | Controlo do processo de esvaziamento da câmara |
| `PLC_Porta_Jusante` | Comando da porta do lado de jusante |
| `PLC_Porta_Montante` | Comando da porta do lado de montante |

**Estrutura de ficheiros markdown para AnythingLLM (um ficheiro por PLC por tipo):**
```
Crestuma/
├── PLC_Comando/
│   ├── tags_digitais_comando.md       ← entradas/saídas digitais (%I, %Q BOOL)
│   ├── tags_analogicas_comando.md     ← entradas/saídas analógicas (INT, WORD, REAL)
│   ├── memorias_comando.md            ← merkers/memórias (%M, %DB)
│   ├── hardware_comando.md            ← CPU, módulos, slots, IPs
│   └── blocos_comando.md              ← OBs, FBs, FCs, DBs com lógica
├── PLC_Enchimento/
│   ├── tags_digitais_enchimento.md
│   ├── tags_analogicas_enchimento.md
│   ├── memorias_enchimento.md
│   ├── hardware_enchimento.md
│   └── blocos_enchimento.md
... (mesmo padrão para Esvaziamento, Porta_Jusante, Porta_Montante)
```

**Coleções no AnythingLLM:**
- Uma coleção por PLC: `Crestuma_Comando`, `Crestuma_Enchimento`, etc.
- OU uma coleção global `Crestuma` com todos os ficheiros (para queries cross-PLC)

**Convenção de naming dos ficheiros:**
- `tags_digitais_<plc>.md`
- `tags_analogicas_<plc>.md`
- `memorias_<plc>.md`
- `hardware_<plc>.md`
- `blocos_<plc>.md`

Tarefa: $ARGUMENTS
