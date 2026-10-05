using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Network.Tests;

public sealed class NetworkPolicyBuilderTests
{
    [Fact]
    public async Task Builder_materializes_wallpapers_as_content_addressed_assets()
    {
        var root = Path.Combine(Path.GetTempPath(), "WallpaperScheduler.Network.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "sample.jpg");
            await File.WriteAllBytesAsync(image, [1, 2, 3, 4, 5, 6]);

            var config = new AppConfig
            {
                Rules =
                [
                    new WallpaperRule
                    {
                        Name = "Rede",
                        DaysOfWeek = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>()),
                        Start = new TimeOnly(0, 0),
                        End = new TimeOnly(23, 59),
                        Scope = WallpaperScope.AllMonitors,
                        Source = new WallpaperSource
                        {
                            Items = [new WallpaperSourceItem { Kind = WallpaperSourceKind.File, Path = image }]
                        }
                    }
                ]
            };

            var builder = new NetworkPolicyBuilder(new MemoryStore(config), new NullLogger());
            var built = await builder.BuildAsync("default");

            Assert.Single(built.Envelope.Assets);
            Assert.Single(built.Envelope.Rules);
            var remotePath = built.Envelope.Rules[0].Source!.Items[0].Path;
            Assert.StartsWith(NetworkPolicyBuilder.AssetSchemePrefix, remotePath);
            Assert.True(built.AssetPaths.ContainsKey(built.Envelope.Assets[0].Id));
            Assert.False(string.IsNullOrWhiteSpace(built.Envelope.Revision));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Per_monitor_only_rule_is_disabled_for_remote_agents()
    {
        var config = new AppConfig
        {
            Rules =
            [
                new WallpaperRule
                {
                    Name = "Local por monitor",
                    Scope = WallpaperScope.PerMonitor,
                    Source = new WallpaperSource()
                }
            ]
        };

        var builder = new NetworkPolicyBuilder(new MemoryStore(config), new NullLogger());
        var built = await builder.BuildAsync("default");

        Assert.False(built.Envelope.Rules[0].Enabled);
        Assert.Equal(WallpaperScope.AllMonitors, built.Envelope.Rules[0].Scope);
    }

    private sealed class MemoryStore(AppConfig config) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(config);
        public Task SaveAsync(AppConfig value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
