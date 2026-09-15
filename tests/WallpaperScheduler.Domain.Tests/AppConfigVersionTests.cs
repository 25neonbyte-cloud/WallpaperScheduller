using Xunit;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Domain.Tests;

public sealed class AppConfigVersionTests
{
    [Fact]
    public void DefaultConfig_UsesCoreSchemaV2BeforeVisualComfortModules()
    {
        var config = new AppConfig();
        Assert.Equal(2, config.Version);
    }
}
