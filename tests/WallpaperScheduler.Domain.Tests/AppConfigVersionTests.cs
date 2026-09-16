using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class AppConfigVersionTests
{
    [Fact]
    public void New_config_uses_schema_v5_and_preserves_five_daily_phases()
    {
        var config = new AppConfig();

        Assert.Equal(5, config.Version);
        Assert.NotNull(config.Ui);
        Assert.Equal(ApplicationThemeMode.FollowSystem, config.Ui.Theme);
        Assert.NotNull(config.VisualComfort);
        Assert.False(config.VisualComfort.Enabled);
        Assert.Equal(VisualControlMode.Scheduled, config.VisualComfort.Temperature.ControlMode);
        Assert.Equal(
            ["Madrugada", "Manhã", "Dia", "Tarde", "Noite"],
            config.Rules.OrderBy(x => x.Order).Select(x => x.Name).ToArray());
    }
}
