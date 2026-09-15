using WallpaperScheduler.Application;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class WallpaperFileSupportTests
{
    [Theory]
    [InlineData("wallpaper.jpg")]
    [InlineData("wallpaper.JPEG")]
    [InlineData("wallpaper.png")]
    [InlineData("wallpaper.BMP")]
    public void Supported_extensions_are_accepted(string path) =>
        Assert.True(WallpaperFileSupport.IsSupportedExtension(path));

    [Theory]
    [InlineData("wallpaper.webp")]
    [InlineData("wallpaper.gif")]
    [InlineData("wallpaper.txt")]
    [InlineData("wallpaper")]
    public void Unsupported_extensions_are_rejected(string path) =>
        Assert.False(WallpaperFileSupport.IsSupportedExtension(path));
}
