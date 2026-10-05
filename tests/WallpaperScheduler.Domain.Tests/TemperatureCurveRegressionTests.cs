using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class TemperatureCurveRegressionTests
{
    [Fact]
    public async Task Stale_manual_mode_does_not_bypass_continuous_night_curve()
    {
        var config = new AppConfig
        {
            VisualComfort = new VisualComfortSettings
            {
                Enabled = true,
                Temperature = new ColorTemperatureSettings
                {
                    Enabled = true,
                    ControlMode = VisualControlMode.Manual, // persisted by an older build
                    ManualKelvin = 6500,
                    DayKelvin = 6500,
                    NightKelvin = 3400,
                    Method = TemperatureApplicationMethod.Software
                }
            }
        };

        var monitor = new MonitorInfo("MONITOR", "Tela", 1920, 1080, Left: 0, Top: 0);
        var profile = new MonitorProfile { Name = "Tela" };
        config.MonitorProfiles.Add(profile);
        var temperature = new FakeTemperatureService();

        var orchestrator = new VisualComfortOrchestrator(
            new FakeConfigStore(config),
            new RuleEngine(),
            new FakeMonitorService(monitor),
            new FakeResolver(new MonitorResolution(profile.Id, profile.Name, monitor, false, "Ativo")),
            new FakeThemeService(),
            temperature,
            new FakeClock(new DateTimeOffset(2026, 9, 16, 1, 0, 0, TimeSpan.FromHours(-3))),
            new FakeLogger());

        await orchestrator.ReapplyCurrentAsync();

        Assert.Single(temperature.LastRequests);
        Assert.Equal(3400, temperature.LastRequests[0].Kelvin);
    }

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
        public SystemThemeMode GetCurrentMode() => SystemThemeMode.Light;
        public SystemThemeApplyResult Apply(SystemThemeMode mode) => new(true, "ok", mode);
        public void Restore() { }
        public void Dispose() { }
    }

    private sealed class FakeTemperatureService : IColorTemperatureService
    {
        public IReadOnlyList<TemperatureMonitorRequest> LastRequests { get; private set; } = [];
        public IReadOnlyList<TemperatureMonitorCapability> GetCapabilities(IReadOnlyList<MonitorResolution> resolutions) => [];
        public ColorTemperatureApplyResult Apply(IReadOnlyList<TemperatureMonitorRequest> requests, bool forceSoftwareConflict)
        {
            LastRequests = requests.ToList();
            return new(true, requests.Select(x => new TemperatureMonitorApplyStatus(
                x.ProfileId, x.ProfileName, x.Method, x.Kelvin, x.Kelvin, true, false, false, "ok")).ToList(), "ok");
        }
        public void Restore() { }
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
