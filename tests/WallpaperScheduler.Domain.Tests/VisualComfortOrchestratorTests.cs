using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class VisualComfortOrchestratorTests
{
    [Fact]
    public async Task Routine_binding_overrides_manual_theme_and_temperature()
    {
        var rule = CreateRule();
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                SystemTheme = new SystemThemeSettings { Enabled = true, ManualMode = SystemThemeMode.Light },
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    Method = TemperatureApplicationMethod.Software,
                    DayKelvin = 6500,
                    NightKelvin = 4200
                },
                Routine = new VisualRoutineSettings
                {
                    Enabled = true,
                    Bindings = new Dictionary<Guid, VisualRoutineBinding>
                    {
                        [rule.Id] = new() { Theme = VisualRoutineThemeTarget.Dark, TemperatureKelvin = 4200 }
                    }
                }
            },
            Rules = [rule]
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var orchestrator = Create(config, theme, temperature);

        var result = await orchestrator.ReapplyCurrentAsync();

        Assert.True(result.Applied);
        Assert.Equal(SystemThemeMode.Dark, theme.LastApplied);
        Assert.Single(temperature.LastRequests);
        Assert.Equal(4200, temperature.LastRequests[0].Kelvin);
        Assert.Equal(TemperatureApplicationMethod.Software, temperature.LastRequests[0].Method);
    }

    [Fact]
    public async Task Scheduled_theme_uses_current_machine_clock_period()
    {
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                SystemTheme = new SystemThemeSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    ManualMode = SystemThemeMode.Light,
                    LightStart = new TimeOnly(7, 0),
                    DarkStart = new TimeOnly(19, 0)
                }
            },
            Rules = [CreateRule()]
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var orchestrator = Create(config, theme, temperature, new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero));

        await orchestrator.ReapplyCurrentAsync();

        Assert.Equal(SystemThemeMode.Dark, theme.LastApplied);
    }

    [Fact]
    public async Task Scheduled_temperature_uses_current_clock_position_on_continuous_curve()
    {
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    Method = TemperatureApplicationMethod.Software,
                    DayKelvin = 6500,
                    NightKelvin = 4000
                }
            }
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();

        // 18:30 é exatamente o meio do período Tarde (17:00–20:00).
        // SmoothStep(0,5) = 0,5, logo a curva deve estar exatamente entre 6500K e 4000K.
        var orchestrator = Create(config, theme, temperature, new DateTimeOffset(2026, 9, 15, 18, 30, 0, TimeSpan.Zero));

        await orchestrator.ApplyCurrentAsync();

        Assert.Single(temperature.LastRequests);
        Assert.Equal(5250, temperature.LastRequests[0].Kelvin);
    }

    [Fact]
    public async Task Morning_curve_returns_progressively_to_neutral()
    {
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    Method = TemperatureApplicationMethod.Software,
                    DayKelvin = 6500,
                    NightKelvin = 4000
                }
            }
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var orchestrator = Create(config, theme, temperature, new DateTimeOffset(2026, 9, 15, 7, 30, 0, TimeSpan.Zero));

        await orchestrator.ApplyCurrentAsync();

        Assert.Single(temperature.LastRequests);
        Assert.Equal(5250, temperature.LastRequests[0].Kelvin);
    }

    [Fact]
    public async Task Routine_explicit_value_still_overrides_single_full_day_period()
    {
        var rule = CreateRule();
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                SystemTheme = new SystemThemeSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    LightStart = new TimeOnly(7, 0),
                    DarkStart = new TimeOnly(19, 0)
                },
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    Method = TemperatureApplicationMethod.Software,
                    DayKelvin = 6500,
                    NightKelvin = 4000
                },
                Routine = new VisualRoutineSettings
                {
                    Enabled = true,
                    Bindings = new Dictionary<Guid, VisualRoutineBinding>
                    {
                        [rule.Id] = new() { Theme = VisualRoutineThemeTarget.Light, TemperatureKelvin = 4700 }
                    }
                }
            },
            Rules = [rule]
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var orchestrator = Create(config, theme, temperature, new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero));

        await orchestrator.ReapplyCurrentAsync();

        Assert.Equal(SystemThemeMode.Light, theme.LastApplied);
        Assert.Single(temperature.LastRequests);
        Assert.Equal(4700, temperature.LastRequests[0].Kelvin);
    }

    [Fact]
    public async Task Disabled_master_restores_owned_visual_state()
    {
        var config = new AppConfig { VisualComfort = new VisualComfortSettings { Enabled = false }, Rules = [CreateRule()] };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var orchestrator = Create(config, theme, temperature);

        var result = await orchestrator.ReapplyCurrentAsync();

        Assert.False(result.Applied);
        Assert.Empty(temperature.LastRequests);
        Assert.Null(theme.LastApplied);
    }

    [Fact]
    public async Task Per_monitor_method_is_forwarded_to_temperature_backend()
    {
        var rule = CreateRule();
        var profileId = Guid.NewGuid();
        var config = new AppConfig
        {
            MonitorProfiles = [new MonitorProfile { Id = profileId, Name = "Tela" }],
            Rules = [rule],
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Scheduled,
                    Method = TemperatureApplicationMethod.Software,
                    DayKelvin = 6500,
                    NightKelvin = 4200,
                    PerMonitorMethods = new Dictionary<Guid, TemperatureApplicationMethod>
                    {
                        [profileId] = TemperatureApplicationMethod.DdcCi
                    }
                }
            }
        };
        var theme = new FakeThemeService();
        var temperature = new FakeTemperatureService();
        var monitor = new MonitorInfo("MONITOR", "Tela", 1920, 1080, Left: 0, Top: 0);
        var orchestrator = new VisualComfortOrchestrator(
            new FakeConfigStore(config),
            new RuleEngine(),
            new FakeMonitorService(monitor),
            new FakeResolver(new MonitorResolution(profileId, "Tela", monitor, false, "Ativo")),
            theme,
            temperature,
            new FakeClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)),
            new FakeLogger());

        await orchestrator.ReapplyCurrentAsync();

        Assert.Single(temperature.LastRequests);
        Assert.Equal(TemperatureApplicationMethod.DdcCi, temperature.LastRequests[0].Method);
    }

    private static VisualComfortOrchestrator Create(
        AppConfig config,
        FakeThemeService theme,
        FakeTemperatureService temperature,
        DateTimeOffset? now = null)
    {
        var monitor = new MonitorInfo("MONITOR", "Tela", 1920, 1080, Left: 0, Top: 0);
        var profile = config.MonitorProfiles.FirstOrDefault();
        if (profile is null)
        {
            profile = new MonitorProfile { Name = "Tela" };
            config.MonitorProfiles.Add(profile);
        }

        return new VisualComfortOrchestrator(
            new FakeConfigStore(config),
            new RuleEngine(),
            new FakeMonitorService(monitor),
            new FakeResolver(new MonitorResolution(profile.Id, profile.Name, monitor, false, "Ativo")),
            theme,
            temperature,
            new FakeClock(now ?? new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)),
            new FakeLogger());
    }

    private static WallpaperRule CreateRule() => new()
    {
        Name = "Dia",
        Enabled = true,
        Priority = 100,
        Order = 0,
        DaysOfWeek = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>()),
        Start = new TimeOnly(0, 0),
        End = new TimeOnly(23, 59)
    };

    private sealed class FakeConfigStore(AppConfig config) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(config);
        public Task SaveAsync(AppConfig value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMonitorService(params MonitorInfo[] monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> GetActiveMonitors() => monitors;
    }

    private sealed class FakeResolver(params MonitorResolution[] resolutions) : IMonitorProfileResolver
    {
        public Task<IReadOnlyList<MonitorResolution>> ResolveAsync(AppConfig config, IReadOnlyList<MonitorInfo> monitors, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MonitorResolution>>(resolutions);
    }

    private sealed class FakeThemeService : ISystemThemeService
    {
        public SystemThemeMode? LastApplied { get; private set; }
        public SystemThemeMode GetCurrentMode() => LastApplied ?? SystemThemeMode.Light;
        public SystemThemeApplyResult Apply(SystemThemeMode mode)
        {
            LastApplied = mode;
            return new(true, "aplicado", mode);
        }
        public void Restore() => LastApplied = null;
        public void Dispose() { }
    }

    private sealed class FakeTemperatureService : IColorTemperatureService
    {
        public IReadOnlyList<TemperatureMonitorRequest> LastRequests { get; private set; } = [];
        public IReadOnlyList<TemperatureMonitorCapability> GetCapabilities(IReadOnlyList<MonitorResolution> resolutions) => [];
        public ColorTemperatureApplyResult Apply(IReadOnlyList<TemperatureMonitorRequest> requests, bool forceSoftwareConflict)
        {
            LastRequests = requests.ToList();
            return new(requests.Count > 0, requests.Select(x => new TemperatureMonitorApplyStatus(
                x.ProfileId, x.ProfileName, x.Method, x.Kelvin, x.Kelvin, true, false, false, "aplicado")).ToList(), "aplicado");
        }
        public void Restore() => LastRequests = [];
        public void Dispose() { }
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }

    private sealed class FakeLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
