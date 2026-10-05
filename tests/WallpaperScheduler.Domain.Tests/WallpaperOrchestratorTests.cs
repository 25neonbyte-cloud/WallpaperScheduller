using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class WallpaperOrchestratorTests
{
    [Fact]
    public async Task PerMonitor_assigns_each_active_profile_its_own_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "WallpaperSchedulerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstImage = Path.Combine(root, "first.jpg");
        var secondImage = Path.Combine(root, "second.jpg");
        await File.WriteAllBytesAsync(firstImage, [0]);
        await File.WriteAllBytesAsync(secondImage, [0]);

        try
        {
            var firstProfile = new MonitorProfile { Name = "Esquerdo" };
            var secondProfile = new MonitorProfile { Name = "Direito" };
            var rule = new WallpaperRule
            {
                Name = "Per monitor",
                Enabled = true,
                DaysOfWeek = [DayOfWeek.Monday],
                Start = new TimeOnly(12, 0),
                End = new TimeOnly(13, 0),
                Scope = WallpaperScope.PerMonitor,
                PerMonitorProfiles = new Dictionary<Guid, WallpaperSource>
                {
                    [firstProfile.Id] = Source(firstImage),
                    [secondProfile.Id] = Source(secondImage)
                }
            };

            var config = new AppConfig
            {
                MonitorProfiles = [firstProfile, secondProfile],
                Rules = [rule]
            };

            var monitors = new[]
            {
                new MonitorInfo("device-left", "Monitor 1", 1920, 1080, "DISPLAY#LEFT"),
                new MonitorInfo("device-right", "Monitor 2", 1920, 1080, "DISPLAY#RIGHT")
            };
            var resolutions = new[]
            {
                new MonitorResolution(firstProfile.Id, firstProfile.Name, monitors[0], false, "ok"),
                new MonitorResolution(secondProfile.Id, secondProfile.Name, monitors[1], false, "ok")
            };

            var applier = new CapturingApplier();
            var orchestrator = new WallpaperOrchestrator(
                new FakeConfigStore(config),
                new RuleEngine(),
                new FakeMonitorService(monitors),
                new FakeResolver(resolutions),
                applier,
                new FixedClock(new DateTimeOffset(2026, 9, 14, 12, 30, 0, TimeSpan.Zero)));

            var result = await orchestrator.ApplyCurrentAsync();

            Assert.True(result.Applied);
            Assert.NotNull(applier.State);
            Assert.Equal(2, applier.State!.Assignments.Count);
            Assert.Contains(applier.State.Assignments, x => x.MonitorId == "device-left" && x.ImagePath == firstImage);
            Assert.Contains(applier.State.Assignments, x => x.MonitorId == "device-right" && x.ImagePath == secondImage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Random_rotation_uses_every_candidate_once_before_starting_a_new_cycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "WallpaperSchedulerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var images = Enumerable.Range(1, 4)
            .Select(index => Path.Combine(root, $"random-{index}.jpg"))
            .ToArray();

        foreach (var image in images)
            await File.WriteAllBytesAsync(image, [0]);

        try
        {
            var rule = new WallpaperRule
            {
                Name = "Random without repeats",
                Enabled = true,
                DaysOfWeek = [DayOfWeek.Monday],
                Start = new TimeOnly(12, 0),
                End = new TimeOnly(13, 0),
                Scope = WallpaperScope.AllMonitors,
                Source = Source(images),
                RotationMode = WallpaperRotationMode.Random,
                RotationIntervalMinutes = 1
            };
            var config = new AppConfig { Rules = [rule] };
            var monitors = new[] { new MonitorInfo("device", "Monitor", 1920, 1080) };
            var observed = new List<string>();

            for (var minute = 0; minute < 8; minute++)
            {
                var applier = new CapturingApplier();
                var orchestrator = new WallpaperOrchestrator(
                    new FakeConfigStore(config),
                    new RuleEngine(),
                    new FakeMonitorService(monitors),
                    new FakeResolver([]),
                    applier,
                    new FixedClock(new DateTimeOffset(2026, 9, 14, 12, minute, 0, TimeSpan.Zero)));

                var result = await orchestrator.ApplyCurrentAsync();

                Assert.True(result.Applied);
                Assert.NotNull(applier.State);
                observed.Add(applier.State!.Assignments.Single().ImagePath);
            }

            var expected = images.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(expected.SetEquals(observed.Take(images.Length)));
            Assert.True(expected.SetEquals(observed.Skip(images.Length).Take(images.Length)));
            Assert.Equal(images.Length, observed.Take(images.Length).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(images.Length, observed.Skip(images.Length).Take(images.Length).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReapplyCurrent_forces_wallpaper_even_when_desired_state_is_unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "WallpaperSchedulerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var image = Path.Combine(root, "wallpaper.jpg");
        await File.WriteAllBytesAsync(image, [0]);

        try
        {
            var rule = new WallpaperRule
            {
                Name = "Current",
                Enabled = true,
                DaysOfWeek = [DayOfWeek.Monday],
                Start = new TimeOnly(12, 0),
                End = new TimeOnly(13, 0),
                Scope = WallpaperScope.AllMonitors,
                Source = Source(image)
            };
            var config = new AppConfig { Rules = [rule] };
            var monitors = new[] { new MonitorInfo("device", "Monitor", 1920, 1080) };
            var applier = new CapturingApplier();
            var orchestrator = new WallpaperOrchestrator(
                new FakeConfigStore(config),
                new RuleEngine(),
                new FakeMonitorService(monitors),
                new FakeResolver([]),
                applier,
                new FixedClock(new DateTimeOffset(2026, 9, 14, 12, 30, 0, TimeSpan.Zero)));

            var first = await orchestrator.ApplyCurrentAsync();
            var unchanged = await orchestrator.ApplyCurrentAsync();
            var forced = await orchestrator.ReapplyCurrentAsync();

            Assert.True(first.Applied);
            Assert.False(unchanged.Applied);
            Assert.True(forced.Applied);
            Assert.Equal(2, applier.ApplyCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WallpaperSource Source(params string[] paths) => new()
    {
        Items = paths
            .Select(path => new WallpaperSourceItem { Kind = WallpaperSourceKind.File, Path = path })
            .ToList()
    };

    private sealed class FakeConfigStore(AppConfig config) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(config);
        public Task SaveAsync(AppConfig value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMonitorService(IReadOnlyList<MonitorInfo> monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> GetActiveMonitors() => monitors;
    }

    private sealed class FakeResolver(IReadOnlyList<MonitorResolution> resolutions) : IMonitorProfileResolver
    {
        public Task<IReadOnlyList<MonitorResolution>> ResolveAsync(AppConfig config, IReadOnlyList<MonitorInfo> monitors, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolutions);
    }

    private sealed class CapturingApplier : IWallpaperApplier
    {
        public WallpaperState? State { get; private set; }
        public int ApplyCount { get; private set; }

        public void Apply(WallpaperState state)
        {
            State = state;
            ApplyCount++;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }
}
