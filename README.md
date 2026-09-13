# Wallpaper Scheduler

Aplicativo Windows 11 para troca automática de wallpapers por regras de dia, horário e monitor.

## Baseline técnica

- C# / .NET 10 LTS
- WPF
- MVVM + Dependency Injection
- JSON local versionado
- `IDesktopWallpaper` / COM
- Multi-monitor
- GitHub Actions em Windows

A especificação em `docs/SPECIFICATION.md` é a fonte de verdade do projeto.

## Estrutura

- `src/WallpaperScheduler.Domain` — regras e modelos puros
- `src/WallpaperScheduler.Application` — orquestração e contratos
- `src/WallpaperScheduler.Infrastructure` — Windows, COM e persistência
- `src/WallpaperScheduler.App` — WPF/MVVM
- `tests/WallpaperScheduler.Domain.Tests` — testes do motor de regras

## CI

O workflow `.github/workflows/ci.yml` restaura dependências, compila, executa testes e publica um artefato Windows x64.
