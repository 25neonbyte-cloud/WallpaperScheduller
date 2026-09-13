# Wallpaper Scheduler — Especificação Mestre

**Versão:** 2.1  
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
- Wallpaper só é reaplicado quando o estado desejado muda.
- Monitor ausente não invalida os demais.
- Configuração utiliza JSON local versionado e backup.
- Identificadores técnicos do Windows não são expostos como configuração obrigatória ao usuário.
- O ciclo diário fornecido pelo aplicativo é somente um padrão inicial; sua estrutura é 100% personalizável.

## Ciclo diário padrão

Uma instalação/configuração nova oferece inicialmente cinco períodos editáveis:

1. `00:00 → 05:30` — **Após meia-noite**
2. `05:30 → 10:00` — **Nascer do sol**
3. `10:00 → 16:00` — **Dia claro**
4. `16:00 → 18:45` — **Pôr do sol**
5. `18:45 → 00:00` — **Noite**

Os nomes, horários, quantidade de períodos, ordem, prioridade, dias, fontes, estilo e rotação podem ser alterados. Períodos podem ser criados e removidos livremente.

A intenção visual do preset é apenas orientativa: manhã confortável, dia claro/vibrante, pôr do sol aquecido e noite de alto contraste/baixa luminosidade. O aplicativo não classifica nem altera automaticamente as cores das imagens no MVP; o usuário associa as coleções desejadas a cada período.

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

Uma regra pode receber:

- um ou vários arquivos;
- uma ou várias pastas;
- combinação de arquivos e pastas;
- inclusão opcional de subpastas.

A UI oferece área de **arrastar e soltar**. Os arquivos não são copiados para o aplicativo; seus caminhos originais são persistidos. Fonte removida ou inacessível gera estado de indisponibilidade sem derrubar o restante da configuração.

Formatos do MVP: JPG/JPEG, PNG e BMP.

### Rotação por regra/período

Cada regra escolhe independentemente:

- `Sequential`: percorre a coleção em ordem determinística;
- `Random`: seleciona de forma pseudoaleatória por janela de tempo.

Cada regra possui seu próprio `rotationIntervalMinutes`. Valor vazio/nulo significa trocar somente na entrada da regra/período (ou quando outra condição exigir reaplicação).

A rotação sequencial é ancorada no início real do período. Exemplo: em `18:43→13:00`, com intervalo de 1 minuto e nove imagens, a sequência é `1→2→...→9→1...` enquanto o período permanecer vigente, inclusive após a meia-noite.

## Multi-monitor

A configuração de produto utiliza **perfis lógicos de monitor**, não `MONITOR_DEVICE_PATH` diretamente.

Fluxo:

`perfil lógico → binding local → monitor físico detectado → device path atual → IDesktopWallpaper`

O binding deve tentar reconhecer monitores por identidade de hardware persistente quando disponível (fabricante/modelo/serial/EDID ou equivalente) e usar device path apenas como vínculo operacional atual. Em nova máquina ou hardware ambíguo, o usuário pode associar o perfil lógico uma vez e o aplicativo memoriza essa relação localmente.

- `AllMonitors`: uma fonte/estado para todos os monitores ativos.
- `PerMonitor`: fontes independentes por perfil lógico.
- Monitor ausente é ignorado temporariamente.
- Monitor que retorna deve receber novamente a configuração vigente.

## Aplicação

A infraestrutura Windows utiliza `IDesktopWallpaper`/COM. Domain e Application não dependem diretamente da API COM.

Fluxo:

`evento/horário → detectar/reconciliar monitores → avaliar regras → resolver fonte/rotação → construir desired state → comparar → aplicar diferenças → registrar resultado`

## Scheduler alvo do MVP

Processo residente em System Tray, inicialização opcional com Windows, eventos de display/sessão/energia, próxima transição calculada, próxima rotação calculada e heartbeat de segurança.

Na etapa atual existe um loop residente de avaliação a cada 30 segundos, suficiente para validar trocas automáticas por minuto. Ele será substituído/refinado pelo scheduler orientado a eventos e próxima transição antes do fechamento do MVP.

## UI alvo do MVP

- lista/editor de períodos/regras;
- criar, editar, excluir e reordenar;
- dias da semana, início/fim, prioridade e estilo;
- modo sequencial/aleatório e intervalo individual;
- drag-and-drop de imagens e pastas;
- configuração global ou por monitor lógico;
- preview e validação de fontes;
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
- Nenhuma regra vigente significa não alterar o wallpaper.
- Imagem/pasta removida invalida somente a fonte afetada.
- CI deve restaurar, compilar e testar em Windows.

## Estado da implementação

- `v0.1`: vertical slice — Domain, Rule Engine, JSON, COM `IDesktopWallpaper`, WPF/MVVM, Apply Now, testes e CI.
- `v0.2`: diagnóstico e validação real de múltiplos monitores.
- `v0.3`: schema v2, ciclo diário editável, arquivos/pastas, rotação sequencial/aleatória por período e editor WPF com drag-and-drop.
- `v0.3.x`: máscara HH:mm, rotação ancorada no início do período, suporte contínuo a períodos atravessando meia-noite, precedência explícita em sobreposição e loop residente de avaliação.
- Próximos blocos: binding persistente de perfis lógicos de monitor, tray/eventos, migração de configuração, acabamento/importação/exportação.
