# Wallpaper Scheduler — Especificação Mestre

**Versão:** 2.3  
**Plataforma:** Windows 11  
**Stack:** C# / .NET 10 LTS / WPF  
**Arquitetura:** MVVM + DI

Este documento é a fonte de verdade do projeto. Código ou implementação que conflite com esta especificação deve ser considerado incorreto até atualização formal desta especificação.

## Objetivo

Trocar wallpapers automaticamente conforme regras configuráveis de dias, horários, fontes e monitores, operando localmente, sem conta, internet, telemetria ou privilégios administrativos.

## Invariantes

- Operação local e como usuário comum.
- Mesma configuração + data/hora + monitores deve produzir o mesmo resultado.
- Regra inválida não derruba o motor.
- Wallpaper só é reaplicado quando o estado desejado muda, salvo reaplicação explícita (`Aplicar agora`) ou recuperação orientada a evento do sistema.
- Monitor ausente não invalida os demais.
- Configuração utiliza JSON local versionado e backup.
- Identificadores técnicos do Windows não são expostos como configuração obrigatória ao usuário.
- O ciclo diário fornecido pelo aplicativo é somente um padrão inicial; sua estrutura é 100% personalizável.
- Inicialização com o Windows vem habilitada por padrão em uma configuração nova; se o usuário desativar explicitamente, essa escolha deve persistir.

## Ciclo diário padrão

Uma instalação/configuração nova oferece inicialmente cinco períodos editáveis:

1. `00:00 → 05:30` — **Após meia-noite**
2. `05:30 → 10:00` — **Nascer do sol**
3. `10:00 → 16:00` — **Dia claro**
4. `16:00 → 18:45` — **Pôr do sol**
5. `18:45 → 00:00` — **Noite**

Os nomes, horários, quantidade de períodos, ordem, prioridade, dias, fontes, estilo e rotação podem ser alterados. Períodos podem ser criados e removidos livremente.

## Motor de regras

Cada regra possui `enabled`, `priority`, `order`, `daysOfWeek`, `start`, `end`, `style`, `scope`, fonte(s), modo de rotação e intervalo opcional.

Resolução determinística:

1. ignorar desativadas;
2. ignorar estruturalmente inválidas;
3. filtrar por dia/horário;
4. ordenar por `priority DESC`;
5. em sobreposição de mesma prioridade, o período configurado mais tarde vence (`order DESC`);
6. desempatar por `id ASC`.

Intervalos usam `[start,end)`. Se `end < start`, o intervalo atravessa meia-noite e `daysOfWeek` representa o dia em que ele começa. Assim, domingo `18:43→13:00` permanece válido durante a madrugada e até segunda às `12:59`, deixando de valer exatamente às `13:00`.

Sobreposições não são silenciosas: a avaliação deve informar quando existem múltiplos períodos ativos e qual deles obteve precedência.

## Fontes de wallpaper

Uma regra pode receber um ou vários arquivos, uma ou várias pastas, combinação de arquivos e pastas e inclusão opcional de subpastas. A UI oferece área de arrastar e soltar; os arquivos não são copiados para o aplicativo.

Formatos do MVP: JPG/JPEG, PNG e BMP.

### Rotação por regra/período

Cada regra escolhe independentemente `Sequential` ou `Random` e possui seu próprio `rotationIntervalMinutes`. Valor vazio/nulo significa trocar somente na entrada da regra/período.

A rotação sequencial é ancorada no início real do período. Exemplo: em `18:43→13:00`, com intervalo de 1 minuto e nove imagens, a sequência é `1→2→...→9→1...` enquanto o período permanecer vigente, inclusive após a meia-noite.

## Multi-monitor

A configuração de produto utiliza **perfis lógicos de monitor**, não `MONITOR_DEVICE_PATH` diretamente.

Fluxo:

`perfil lógico → binding local → monitor físico detectado → device path atual → IDesktopWallpaper`

A configuração portátil (`config.json`) armazena somente `MonitorProfile.Id` e `MonitorProfile.Name`. Dados físicos da máquina ficam separados em `%LOCALAPPDATA%\WallpaperScheduler\monitor-bindings.json`.

Reconciliação local:

1. tentar o último device path conhecido;
2. se necessário, tentar identidade de hardware conservadora;
3. usar resolução somente quando ela identifica um único monitor restante;
4. se dois monitores forem indistinguíveis, não trocar associações silenciosamente: marcar como ambíguo;
5. monitor ativo sem perfil recebe um novo perfil lógico local, que pode ser renomeado pelo usuário.

A identidade de hardware atual é deliberadamente conservadora e deriva da família/modelo expostos pelo device path. EDID/serial pode ser adicionado posteriormente para aumentar a precisão sem alterar o modelo lógico.

- `AllMonitors`: uma fonte/estado para todos os monitores ativos.
- `PerMonitor`: fontes independentes por perfil lógico.
- Perfis podem ser renomeados na UI sem expor IDs técnicos.
- Monitor ausente é ignorado temporariamente.
- Monitor que retorna deve recuperar seu vínculo conhecido quando a identificação for inequívoca.
- Fonte por monitor referencia `ProfileId`, nunca o device path.

## Aplicação

A infraestrutura Windows utiliza `IDesktopWallpaper`/COM. Domain e Application não dependem diretamente da API COM.

Fluxo:

`evento/horário → detectar/reconciliar monitores → avaliar regras → resolver fonte/rotação → construir desired state → comparar → aplicar diferenças → registrar resultado`

Eventos de recuperação (`display`, retorno de suspensão, desbloqueio/logon de sessão e alteração do relógio) podem forçar a reaplicação do estado desejado mesmo quando ele é igual ao último estado conhecido, pois o Windows pode ter perdido ou alterado externamente o wallpaper durante a transição.

## Scheduler alvo do MVP

Processo residente em System Tray, inicialização com Windows habilitada por padrão e desativável pelo usuário, eventos de display/sessão/energia, próxima transição calculada, próxima rotação calculada e heartbeat de segurança.

Na etapa atual existe um loop residente de avaliação a cada 30 segundos combinado com eventos do Windows para mudança de display, retorno de suspensão, desbloqueio/logon e alteração de data/hora. Os eventos são agrupados por debounce curto antes da reavaliação para evitar tempestades de notificações. O cálculo exato da próxima transição/rotação será refinado no hardening do scheduler.

## UI alvo do MVP

- lista/editor de períodos/regras;
- criar, editar, excluir e reordenar;
- dias da semana, início/fim, prioridade e estilo;
- modo sequencial/aleatório e intervalo individual;
- drag-and-drop de imagens e pastas;
- configuração global ou por monitor lógico;
- renomear perfis lógicos;
- visualizar estado conectado/ausente/ambíguo sem exigir IDs técnicos;
- Aplicar agora;
- importação/exportação JSON;
- diagnóstico técnico sem exigir IDs do usuário.

## Fora do MVP

Lock screen, Task Scheduler híbrido, download automático, cloud, conta, telemetria, serviço Windows e hacks de política/registro.

## Critérios críticos

- Ao iniciar dentro de uma regra vigente, aplicar essa regra.
- Cada período do preset inicial pode ser alterado/removido sem restrição estrutural.
- Uma pasta com múltiplas imagens deve respeitar o modo e intervalo configurados para sua regra.
- Período que atravessa meia-noite deve continuar vigente até seu horário final do dia seguinte.
- Rotação sequencial deve entrar em loop pela quantidade de imagens enquanto o período estiver vigente.
- Em sobreposição, períodos criados/configurados mais tarde têm precedência quando a prioridade for igual e a UI/status deve informar o conflito.
- Desconectar um monitor não interrompe o outro.
- Reconectar um monitor reaplica sua associação válida.
- Dois monitores idênticos sem identificação suficiente nunca podem ter seus perfis trocados silenciosamente.
- Fontes `PerMonitor` devem seguir o perfil lógico mesmo que o device path mude.
- Retorno de suspensão, desbloqueio/logon, alteração de display e mudança do relógio devem provocar reavaliação automática sem exigir abertura da janela.
- Nenhuma regra vigente significa não alterar o wallpaper.
- Imagem/pasta removida invalida somente a fonte afetada.
- CI deve restaurar, compilar e testar em Windows.

## Estado da implementação

- `v0.1`: vertical slice — Domain, Rule Engine, JSON, COM `IDesktopWallpaper`, WPF/MVVM, Apply Now, testes e CI.
- `v0.2`: diagnóstico e validação real de múltiplos monitores.
- `v0.3`: schema v2, ciclo diário editável, arquivos/pastas, rotação sequencial/aleatória por período e editor WPF com drag-and-drop.
- `v0.3.x`: máscara HH:mm, rotação ancorada no início do período, suporte contínuo a períodos atravessando meia-noite, precedência explícita em sobreposição e loop residente de avaliação.
- `v0.4`: perfis lógicos portáteis, binding físico separado por máquina, reconciliação conservadora e fontes independentes por monitor lógico; validado em máquina real.
- `v0.5.x`: tray, execução residente e inicialização persistente com o Windows; mecanismo Startup validado em máquina real. Inicialização passa a ser padrão para novas configurações.
- `v0.6` em desenvolvimento: eventos do sistema para display, resume, unlock/logon e alteração do relógio, com reavaliação/reaplicação coordenada.
- Próximo bloco: hardening, importação/exportação JSON e preparação de release.
