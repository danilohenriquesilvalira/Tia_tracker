---
description: Estratégia de exportação de tags em multi-ficheiro para RAG otimizado. Uso: /rag-tags [plc ou problema]
---

## Estratégia Multi-ficheiro para RAG de Tags PLC

**Problema:** AnythingLLM (e a maioria dos sistemas RAG) divide documentos em chunks de ~1000 caracteres, ignorando headers markdown. Uma tabela grande de tags digitais partida a meio não tem contexto — o retrieval falha.

**Solução:** Um ficheiro por grupo semântico → cada ficheiro cabe em 1–2 chunks → retrieval perfeito.

---

## Estrutura de pastas gerada pelo TiaTracker

```
<base>/
└── <PLC_NAME>/
    └── tags/
        ├── resumo_equipamentos.md       ← lista de motores, sensores, comportas detectados automaticamente
        ├── di_motores_bombas.md         ← entradas digitais: RM_, PROT_BOMB_, DEF_ARR_
        ├── di_fins_curso.md             ← entradas digitais: FC_ (fins de curso, confirmações)
        ├── di_botoes_comandos.md        ← entradas digitais: BT_, STOP_, RESET_
        ├── di_condicoes_alimentacao.md  ← entradas digitais: COND_, PRES_, EMERG_, PS_, PROT_24_
        ├── di_reservas.md               ← entradas digitais: RESERV_
        ├── do_ordens_marcha.md          ← saídas digitais: OS_, OM_, OA_, OD_
        ├── do_sinalizacoes_comportas.md ← saídas digitais: SIN_BOMB_, SIN_COMP_
        ├── do_sinalizacoes_gerais.md    ← saídas digitais: SIN_AUTOM_, SIN_PRES_
        ├── do_interface_comando.md      ← saídas digitais: INTERF_, INT_
        ├── do_outros.md                 ← saídas digitais: BYPASS_, SEMAF_, CPU_
        ├── mb_defeitos.md               ← memórias: DEF_ (defeitos de equipamento)
        ├── mb_alarmes.md                ← memórias: AL_ (alarmes)
        ├── mb_registos.md               ← memórias: REG_ (registos de eventos)
        ├── mb_impulsos.md               ← memórias: IMP_ (impulsos one-shot)
        ├── mb_interdicoes.md            ← memórias: INTERD_, AUTOR_, COND_
        ├── mb_navegacao.md              ← memórias: ENT_, SAID_ (transições de estado)
        ├── mb_controlo.md               ← memórias: FC_ENCOD_, M_ (controlo interno)
        ├── mb_outros.md                 ← memórias: tudo o resto
        ├── analogicas.md                ← AI + AO (entradas/saídas analógicas)
        └── mw_alarmes_eventos.md        ← MW (words de alarme/evento)
```

---

## Regras de geração de conteúdo por ficheiro

**Cada ficheiro tem:**
1. Header `# <Grupo> — <PLC> (<Instalação>)`
2. Bloco de prose rico (3–5 frases) com linguagem operacional: "bomba", "pistão", "comporta"
3. Tabela com colunas: `Tag | Endereço | Tipo | Significado`
4. Endereços em formato humano: `Entrada I3.2` em vez de `%I3.2` (o símbolo `%` não tem valor semântico nos embeddings)

**`resumo_equipamentos.md`** — gerado automaticamente:
- Deteta motores via prefixo `RM_` nas entradas → lista com nome operacional
- Deteta sensores via prefixo `FC_` → lista de fins de curso
- Prose: "Este PLC comanda X motores/bombas e Y sensores de posição"

**`InferTagMeaning`** — função que converte nome de tag em linguagem natural:
- Split por `_`, mapeia tokens para dicionário PT (~60 entradas)
- Ex: `DEF_BOMB_A` → "Defeito — Bomba A"
- Ex: `FC_VALV_AB` → "Fim de Curso — Válvula Abertura"

**`HumanAddress`** — converte endereço para texto embedding-friendly:
- `%I3.2` → `Entrada I3.2`
- `%QW4` → `Saída Word QW4`
- `%M10.0` → `Memória M10.0`

---

## Como invocar no TiaTracker

1. Carregar projeto TIA Portal
2. Expandir o PLC na árvore
3. Clicar com botão direito em **Tag Tables**
4. Selecionar **"📁 Exportar Tags deste PLC para IA (multi-ficheiro)"**
5. Escolher pasta base → cria `<base>/<PLC>/tags/` automaticamente

---

## Upload para AnythingLLM

- Criar workspace por PLC: `Crestuma_Enchimento`, `Crestuma_Comando`, etc.
- Upload de toda a pasta `tags/` de uma vez
- Cada ficheiro = 1–2 chunks = retrieval cirúrgico
- Chunk size recomendado no AnythingLLM: **1000** (máximo permitido)

---

## Próximos passos de RAG

- [ ] Adicionar `blocos_<plc>.md` — lógica FB/FC para diagnóstico causal
- [ ] Adicionar `hardware_<plc>.md` — topologia física (slots, IPs, módulos)
- [ ] Criar `falhas_<plc>.md` — mapeamento mensagem HMI → tag → causa (ver /cross-ref)

---

**Tarefa atual:** $ARGUMENTS
