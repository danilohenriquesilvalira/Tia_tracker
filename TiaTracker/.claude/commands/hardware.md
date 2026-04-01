---
description: Trabalha no HardwareReader ou geração de hardware markdown. Uso: /hardware [problema ou feature]
---

Tarefa: trabalhar na leitura/exportação de hardware do TiaTracker.

**Ficheiro:** `Core/HardwareReader.cs`

**Métodos principais:**
- `ReadAll()` — lê hardware de todos os devices
- `ReadDevice()` — extrai info do CPU (modelo, referência, comentário)
- `ReadItem()` — processa módulos e terminais I/O recursivamente
- `ReadModuleIO()` — mapeia ranges de I/O do módulo (ex: IB0..IB3, QB0..QB3)
- `ReadNetworkInterface()` — extrai IP, subnet, nome PROFINET

**Dados extraídos por módulo:**
- Slot, Modelo, Referência (Order Number)
- Ranges de entradas (IB) e saídas (QB)
- Comentário/descrição
- Configuração de rede (IP, PROFINET name)

**Output no markdown:**
```markdown
| Slot | Módulo | Referência | Entradas | Saídas | Comentário |
|------|--------|------------|----------|--------|------------|
```

**Problema/feature a trabalhar:** $ARGUMENTS
