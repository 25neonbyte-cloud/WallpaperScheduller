using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Domain.Tests;

public sealed class AppConfigVersionTests
{
    [Fact]
    public void New_config_uses_schema_v3()
    {
        var config = new AppConfig();
        Assert.Equal(3, config.Version);
        Assert.NotNull(config.VisualComfort);
        Assert.False(config.VisualComfort.Enabled);
    }
}
