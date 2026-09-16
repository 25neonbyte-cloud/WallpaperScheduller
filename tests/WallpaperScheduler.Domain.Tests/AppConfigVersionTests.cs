using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class AppConfigVersionTests
{
    [Fact]
    public void New_config_uses_schema_v4()
    {
        var config = new AppConfig();
        Assert.Equal(4, config.Version);
        Assert.NotNull(config.Ui);
        Assert.Equal(ApplicationThemeMode.FollowSystem, config.Ui.Theme);
        Assert.NotNull(config.VisualComfort);
        Assert.False(config.VisualComfort.Enabled);
    }
}
