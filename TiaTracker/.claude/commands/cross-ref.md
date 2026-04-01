---
description: Estratégia de diagnóstico IA por cruzamento de tags + lógica PLC + falhas/eventos. Uso: /cross-ref [ideia ou componente]
---

## Visão: Diagnóstico IA por Cruzamento de Camadas

Objetivo: quando um operador reporta uma falha, a IA consegue responder
**"esta falha é gerada pelo bit DEF_PROT_V_DIST_A que ativa quando o sensor FC_VALV_AB não confirma abertura em 30 s — verificar sensor no slot IB4.3"**

---

## As 3 camadas de conhecimento (markdown para RAG)

| Camada | Ficheiro | Conteúdo |
|--------|----------|----------|
| **Tags** | `tags_digitais_<plc>.md` etc. | Endereços, tipos, significado inferido |
| **Lógica** | `blocos_<plc>.md` | FBs/FCs com condições de ativação dos bits DEF_/AL_ |
| **Falhas/Eventos** | `falhas_<plc>.md` | Mensagens HMI/SCADA mapeadas ao bit que as ativa |

---

## Estrutura do ficheiro `falhas_<plc>.md`

```markdown
# Falhas e Eventos — <PLC>

## Falhas de Equipamento

| Código | Mensagem HMI | Tag Associada | Bit PLC | Causa Provável |
|--------|-------------|---------------|---------|----------------|
| F001   | "Defeito Bomba A" | DEF_BOMB_A | %M10.0 | Proteção PROT_BOMB_A ativa ou RM_BOMB_A ausente |
| F002   | "Válvula Dist. não abre" | DEF_PROT_V_DIST_A | %M10.1 | FC_VALV_AB não confirmado em timeout |
...

## Eventos de Operação

| Código | Mensagem | Tag | Condição |
|--------|----------|-----|----------|
| E001   | "Início Enchimento" | REG_INICIO_ENCH | Ordem recebida + COND_ENCH OK |
...
```

---

## Query RAG ideal (exemplo)

> "A mensagem 'Defeito Bomba A' apareceu. O que pode estar a causar isso?"

O modelo vai:
1. Recuperar chunk de `falhas_enchimento.md` → F001 → DEF_BOMB_A = %M10.0
2. Recuperar chunk de `blocos_enchimento.md` → lógica que escreve %M10.0
3. Recuperar chunk de `tags_digitais_enchimento.md` → PROT_BOMB_A = %I2.1, RM_BOMB_A = %I2.0
4. Responder: "verificar contactor K2 (RM_BOMB_A em %I2.0) ou relé de proteção em %I2.1"

---

## Implementação no TiaTracker

- [ ] Exportar ficheiro `falhas_<plc>.md` a partir de HMI alarms (se disponível via API)
- [ ] Ou gerar template vazio e preencher manualmente a partir do SCADA/WinCC
- [ ] Cruzar com `blocos_<plc>.md` já gerado pelo TiaTracker
- [ ] Testar queries no AnythingLLM com as 3 camadas na mesma workspace

---

**Tarefa actual:** $ARGUMENTS
