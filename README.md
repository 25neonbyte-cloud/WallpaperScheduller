# Wallpaper Scheduler

Aplicativo Windows 11 para troca automática de wallpapers por regras de dia, horário e monitor.

## Baseline técnica

- C# / .NET 10 LTS
- WPF
- MVVM + Dependency Injection
- JSON local versionado com backup e recuperação
- `IDesktopWallpaper` / COM
- Multi-monitor por perfis lógicos persistentes
- System Tray e inicialização com Windows
- Eventos de display, sessão, energia e relógio
- Logs locais e importação/exportação JSON
- GitHub Actions em Windows

A especificação em `docs/SPECIFICATION.md` é a fonte de verdade do projeto.

## Estrutura

- `src/WallpaperScheduler.Domain` — regras e modelos puros
- `src/WallpaperScheduler.Application` — orquestração e contratos
- `src/WallpaperScheduler.Infrastructure` — Windows, COM, persistência e diagnóstico
- `src/WallpaperScheduler.App` — WPF/MVVM, tray e ciclo de vida
- `tests/WallpaperScheduler.Domain.Tests` — testes do motor e contratos de comportamento

## Dados locais

O aplicativo não usa conta, internet ou telemetria. Os dados ficam em `%LOCALAPPDATA%\WallpaperScheduler`:

- `config.json` — configuração portátil;
- `config.json.bak` — último backup válido;
- `monitor-bindings.json` — vínculo físico desta máquina;
- `logs\WallpaperScheduler.log` — diagnóstico local com rotação.

## Execução

A inicialização com o Windows vem habilitada por padrão e pode ser desativada pelo menu do tray. Ao iniciar pelo Windows, o aplicativo permanece oculto no tray e continua a programação salva.

## CI

O workflow `.github/workflows/ci.yml` restaura dependências, compila, executa testes, publica o aplicativo self-contained Windows x64, verifica a presença do executável e gera SHA-256 no artefato.
