using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class MonitorProfileResolverTests
{
    [Fact]
    public async Task Creates_logical_profiles_for_unmatched_active_monitors()
    {
        var config = new AppConfig { Rules = [] };
        var configStore = new FakeConfigStore(config);
        var bindingStore = new FakeBindingStore();
        var resolver = new MonitorProfileResolver(bindingStore, configStore);

        var monitors = new[]
        {
            new MonitorInfo("path-a", "Monitor 1", 1920, 1080, "DISPLAY#AAA"),
            new MonitorInfo("path-b", "Monitor 2", 3440, 1440, "DISPLAY#BBB")
        };

        var result = await resolver.ResolveAsync(config, monitors);

        Assert.Equal(2, config.MonitorProfiles.Count);
        Assert.Equal(2, result.Count(x => x.Monitor is not null));
        Assert.Equal(2, bindingStore.Bindings.Count);
        Assert.True(configStore.SaveCount > 0);
    }

    [Fact]
    public async Task Reconnects_profile_by_hardware_key_when_device_path_changes()
    {
        var profile = new MonitorProfile { Name = "Ultrawide" };
        var config = new AppConfig { Rules = [], MonitorProfiles = [profile] };
        var configStore = new FakeConfigStore(config);
        var bindingStore = new FakeBindingStore
        {
            Bindings = [new MonitorBinding(profile.Id, "DISPLAY#MODEL", "old-path", 3440, 1440)]
        };
        var resolver = new MonitorProfileResolver(bindingStore, configStore);

        var result = await resolver.ResolveAsync(
            config,
            [new MonitorInfo("new-path", "Monitor 1", 3440, 1440, "DISPLAY#MODEL")]);

        var resolution = Assert.Single(result);
        Assert.NotNull(resolution.Monitor);
        Assert.Equal("new-path", resolution.Monitor!.Id);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public async Task Identical_monitors_are_not_silently_swapped_when_binding_is_lost()
    {
        var profile = new MonitorProfile { Name = "Monitor principal" };
        var config = new AppConfig { Rules = [], MonitorProfiles = [profile] };
        var configStore = new FakeConfigStore(config);
        var bindingStore = new FakeBindingStore
        {
            Bindings = [new MonitorBinding(profile.Id, "DISPLAY#SAME", "missing-path", 1920, 1080)]
        };
        var resolver = new MonitorProfileResolver(bindingStore, configStore);

        var result = await resolver.ResolveAsync(config,
        [
            new MonitorInfo("new-a", "Monitor 1", 1920, 1080, "DISPLAY#SAME"),
            new MonitorInfo("new-b", "Monitor 2", 1920, 1080, "DISPLAY#SAME")
        ]);

        var original = result.Single(x => x.ProfileId == profile.Id);
        Assert.Null(original.Monitor);
        Assert.True(original.IsAmbiguous);
    }

    private sealed class FakeConfigStore(AppConfig config) : IConfigStore
    {
        public int SaveCount { get; private set; }
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(config);
        public Task SaveAsync(AppConfig value, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBindingStore : IMonitorBindingStore
    {
        public IReadOnlyList<MonitorBinding> Bindings { get; set; } = [];

        public Task<IReadOnlyList<MonitorBinding>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Bindings);

        public Task SaveAsync(IReadOnlyList<MonitorBinding> bindings, CancellationToken cancellationToken = default)
        {
            Bindings = bindings.ToList();
            return Task.CompletedTask;
        }
    }
}
