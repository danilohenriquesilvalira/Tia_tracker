---
description: Modifica a geração de markdown no TiaTracker. Uso: /md-output [secção a modificar]
---

Tarefa: modificar a geração de markdown em `UI/MainForm.cs`.

**Métodos de geração de markdown (todos em MainForm.cs):**
- `BuildMarkdown()` — orquestra tudo, itera por device
- `MdBlock()` — formata um bloco (interface + redes)
- `MdMembers()` — formata membros de struct com tipos
- `MdHardware()` — tabela de topologia de hardware
- `MdFaultIndex()` — lista tags de alarme/falha
- `MdAvailableIO()` — endereços I/O livres
- `MdTagCrossRef()` — cross-reference de uso de tags
- `MdTagsWithoutDescription()` — tags sem comentário
- `MdCallNode()` — nó recursivo do grafo de chamadas

**Convenções do markdown gerado:**
- Headers hierárquicos para chunking semântico no RAG
- Endereços em inline code: `%I0.0`, `%Q1.0`, `DB40.DBX12.3`
- Tabelas para dados estruturados (tags, membros, hardware)
- Code blocks com linguagem: ```scl, ```pascal, ```
- Dual-language context: Técnico + Operador
- Cada rede é uma unidade RAG independente (auto-contida)

**Estrutura de output por PLC (Crestuma):**
- Ficheiros separados por tipo: tags, hardware, blocos
- Um ficheiro por PLC por tipo para RAG granular
- Naming: `tags_digitais_<plc>.md`, `hardware_<plc>.md`, `blocos_<plc>.md`

Secção a modificar: $ARGUMENTS
