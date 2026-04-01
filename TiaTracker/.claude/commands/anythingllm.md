---
description: Configuração e otimização do AnythingLLM para RAG com ficheiros do TiaTracker. Uso: /anythingllm [problema]
---

## AnythingLLM — Configuração para RAG de PLCs

**Stack local:** Ollama + AnythingLLM + modelos locais (sem internet necessária)

---

## Modelos usados

| Função | Modelo | Hardware |
|--------|--------|---------|
| LLM (resposta) | `qwen2.5:3b` | GTX 1650 Ti 4GB VRAM |
| Embeddings | `nomic-embed-text` | CPU |
| Alternativa LLM | `qwen2.5:7b` | ≥6GB VRAM (melhor PT) |

**Nota:** `qwen2.5:3b` é suficiente para lookup de tags mas fraco em raciocínio causal em português. Para queries do tipo "por que razão a bomba não arranca", usar `qwen2.5:7b` se hardware permitir.

---

## Limitações críticas de chunking

**Chunk size máximo: 1000 caracteres** (limite do modelo de embedding)

AnythingLLM corta os documentos em chunks de ≤1000 chars **ignorando** headers markdown. Consequências:
- Uma tabela grande de tags digitais é cortada a meio
- A secção "Entradas Reservadas" pode ficar num chunk sem título → não é retrived
- O glossário no topo consome chunks sem valor semântico

**Solução implementada:** multi-ficheiro (ver `/rag-tags`) — um ficheiro por grupo semântico, cada ficheiro cabe em 1–2 chunks.

---

## Configuração de workspace recomendada

**Por PLC:**
- Workspace: `Crestuma_Enchimento`, `Crestuma_Comando`, etc.
- Upload: pasta `tags/` completa do PLC (20 ficheiros ~)
- Chunk size: 1000 (máximo)

**Global (cross-PLC):**
- Workspace: `Crestuma` com todos os PLCs
- Útil para queries que envolvem múltiplos PLCs

---

## System prompt recomendado para workspace de PLC

```
És um assistente técnico especializado no PLC {NOME_PLC} da {INSTALAÇÃO}.
Respondes sempre em português europeu.
Quando um operador descreve uma falha em linguagem simples (ex: "o pistão não sobe", "a bomba não arranca"), 
identifies o tag PLC correspondente e dás o endereço exato.
Formato das respostas:
- Tag: NOME_TAG
- Endereço: I3.2 (ou M10.0, Q2.1, etc.)
- Significado: descrição em linguagem de operador
- Causa provável: (quando possível)
Nunca inventas tags — só respondes com o que está nos documentos carregados.
```

---

## Estratégia de queries

**Queries que funcionam bem:**
- "Quantas entradas digitais de reserva existem?"
- "Qual é o tag para ligar a bomba A?"
- "Que tag confirma a abertura da comporta?"

**Queries que requerem blocos (futuro):**
- "Por que razão o defeito DEF_BOMB_A está ativo?"
- "O que impede o enchimento de iniciar?"
- "Quais as condições para o pistão subir?"

**Problema de vocabulário operador vs PLC:**
- Operadores dizem: "pistão", "cilindro", "comporta", "maçaneta"
- Tags dizem: `FC_SUBIDA_AB_COMP_A`, `PROT_BOMB_A`
- **Solução:** prose rico em cada ficheiro usando linguagem operacional → embeddings fazem match semântico

---

## Debug de retrieval

Para verificar o que está a ser retrived:
1. AnythingLLM → workspace → ícone de "ver contexto" na resposta
2. Ver quais chunks foram usados
3. Se o chunk certo não aparece → ficheiro demasiado grande ou grupo mal separado

---

**Problema a resolver:** $ARGUMENTS
