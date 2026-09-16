using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class SchedulerSettingsTests
{
    [Fact]
    public void StartWithWindows_is_enabled_by_default()
    {
        var settings = new SchedulerSettings();
        Assert.True(settings.StartWithWindows);
    }
}
