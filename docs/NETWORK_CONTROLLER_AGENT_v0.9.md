# Wallpaper Scheduler — Rede Controller/Agent v0.9

## Objetivo

A versão 0.9 introduz a base distribuída do Wallpaper Scheduler em uma LAN confiável.

- **Standalone**: comportamento local existente.
- **Controller (Mestre)**: continua executando localmente e publica a política atual para outras máquinas.
- **Agent (Agente/Slave)**: recebe a política central, sincroniza wallpapers por conteúdo SHA-256 e executa localmente os mesmos motores de wallpaper e conforto visual.

## Princípios

1. O Controller distribui **política**, não envia comandos de cor a cada tick.
2. Cada Agent calcula localmente wallpaper, tema e curva de temperatura usando os motores existentes.
3. O relógio do Agent recebe um offset atualizado a cada sincronização com o Controller.
4. Se o Controller ou a rede cair, o Agent continua usando a última política válida em cache.
5. Wallpapers são transferidos pela LAN e armazenados em cache local por SHA-256.
6. O Controller é autoridade sobre Rules e VisualComfort; preferências locais de hardware, como método de temperatura por monitor, permanecem no Agent.
7. Perfis físicos/lógicos de monitor não são transportados entre máquinas.
8. Nesta primeira versão distribuída, regras exclusivamente PerMonitor sem Fonte global são desativadas na política remota para impedir mapeamento incorreto entre hardwares diferentes.

## Configuração

Em **Configurações → Rede / Mestre e Agente…**:

- escolha Local, Mestre/Controller ou Agente/Slave;
- defina nome da máquina e grupo;
- no Controller, escolha porta e chave de pareamento;
- no Agent, informe o endereço do Controller no formato `http://IP:porta` e copie a mesma chave;
- reinicie o aplicativo após mudar o papel da máquina.

Porta padrão: **48721**.

## Endpoints v1

- `GET /api/v1/health`
- `GET /api/v1/policy?group=...`
- `GET /api/v1/assets/{sha256}?group=...`
- `POST /api/v1/heartbeat`
- `GET /api/v1/agents`

Política, assets, heartbeat e agents exigem o cabeçalho `X-WS-Key`.

## Persistência

- config local: `%LOCALAPPDATA%\WallpaperScheduler\config.json` — schema v6;
- cache de política: `%LOCALAPPDATA%\WallpaperScheduler\network\policy-cache.json`;
- wallpapers sincronizados: `%LOCALAPPDATA%\WallpaperScheduler\network\assets`.

## Comportamento offline

O Agent não depende do Controller para cada troca. Depois de sincronizar, ele executa localmente a última política válida. Se a rede cair, o scheduler continua. Ao reconectar, revisiona a política e baixa apenas assets que ainda não possui ou que falhem na validação SHA-256.

## Segurança desta etapa

A v0.9 usa uma chave compartilhada para autenticação dentro de uma **LAN privada e confiável**. HTTP não oferece confidencialidade. Antes de uso fora de rede privada, o protocolo deve receber pareamento de identidade e TLS/mTLS ou assinatura forte de mensagens.

## Próximos blocos da arquitetura distribuída

- dashboard de máquinas no Controller;
- grupos com políticas distintas;
- estado online/offline persistente;
- override por grupo, máquina e futuramente monitor;
- discovery automático na LAN;
- pareamento forte/TLS;
- limpeza de assets sem referência;
- aplicação/reconciliação imediata após nova política;
- testes reais com múltiplas máquinas e relógios diferentes.
