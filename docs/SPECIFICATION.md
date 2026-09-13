# Wallpaper Scheduler — Especificação Mestre

**Versão:** 1.0  
**Plataforma:** Windows 11  
**Stack:** C# / .NET 10 LTS / WPF  
**Arquitetura:** MVVM + DI

Este documento é a fonte de verdade do projeto. Código ou implementação que conflite com esta especificação deve ser considerado incorreto até atualização formal desta especificação.

## Objetivo

Trocar wallpapers automaticamente conforme regras configuráveis de dias, horários e monitores, operando localmente, sem conta, internet, telemetria ou privilégios administrativos.

## Invariantes

- Operação local e como usuário comum.
- Mesma configuração + data/hora + monitores deve produzir o mesmo resultado.
- Regra inválida não derruba o motor.
- Wallpaper só é reaplicado quando o estado desejado muda.
- Monitor ausente não invalida os demais.
- Configuração utiliza JSON local versionado e backup.

## Motor de regras

Cada regra possui `enabled`, `priority`, `order`, `daysOfWeek`, `start`, `end`, `style`, `scope` e imagem(ns).

Resolução determinística:

1. ignorar desativadas;
2. ignorar estruturalmente inválidas;
3. filtrar por dia/horário;
4. ordenar por `priority DESC`;
5. desempatar por `order ASC`;
6. desempatar por `id ASC`.

Intervalos usam `[start,end)`. Se `end < start`, o intervalo atravessa meia-noite e `daysOfWeek` representa o dia em que ele começa. Assim, domingo `18:00→06:00` continua válido segunda às 02:00.

## Multi-monitor

- `AllMonitors`: uma imagem para todos os monitores ativos.
- `PerMonitor`: associação persistente por device path retornado por `IDesktopWallpaper`.
- Monitor ausente é ignorado temporariamente.
- Monitor que retorna deve receber novamente a configuração da regra vigente.

## Aplicação

A infraestrutura Windows utiliza `IDesktopWallpaper`/COM. Domain e Application não dependem diretamente da API COM.

Fluxo:

`evento/horário → detectar monitores → avaliar regras → construir desired state → comparar → aplicar diferenças → registrar resultado`

## Scheduler alvo do MVP

Processo residente em System Tray, inicialização opcional com Windows, eventos de display/sessão/energia, próxima transição calculada e heartbeat de segurança padrão de 60 s.

## UI alvo do MVP

Lista de regras, filtros, editor, seleção de dias/horário/prioridade/estilo, seleção de imagem, configuração por monitor, preview, validação, Aplicar agora, importação/exportação JSON e diagnóstico.

## Fora do MVP

Lock screen, Task Scheduler híbrido, download automático, cloud, conta, telemetria, serviço Windows e hacks de política/registro.

## Critérios críticos

- Ao iniciar dentro de uma regra vigente, aplicar essa regra.
- Desconectar um monitor não interrompe o outro.
- Reconectar um monitor reaplica sua associação válida.
- Nenhuma regra vigente significa não alterar o wallpaper.
- Imagem removida invalida somente a associação/regra afetada.
- CI deve restaurar, compilar e testar em Windows.

## Estado da implementação

`v0.1`: vertical slice inicial — Domain, Rule Engine, persistência JSON, COM `IDesktopWallpaper`, detecção de monitores, WPF/MVVM, Apply Now, testes e CI. Scheduler residente, tray e editor completo permanecem para a próxima etapa.
