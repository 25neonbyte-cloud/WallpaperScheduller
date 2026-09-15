using System.Runtime.InteropServices;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsWallpaperService : IMonitorService, IWallpaperApplier
{
    public IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var api = Create();
        try
        {
            api.GetMonitorDevicePathCount(out var count);
            var monitors = new List<MonitorInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                api.GetMonitorDevicePathAt(i, out var id);
                api.GetMonitorRECT(id, out var rect);
                monitors.Add(new(
                    id,
                    $"Monitor {i + 1}",
                    rect.Right - rect.Left,
                    rect.Bottom - rect.Top,
                    ExtractHardwareKey(id)));
            }
            return monitors;
        }
        finally { Release(api); }
    }

    public void Apply(WallpaperState state)
    {
        var api = Create();
        try
        {
            api.SetPosition(Map(state.Style));
            foreach (var assignment in state.Assignments)
                api.SetWallpaper(assignment.MonitorId, assignment.ImagePath);
        }
        finally { Release(api); }
    }

    private static string? ExtractHardwareKey(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        var normalized = devicePath.Replace('/', '\\').Trim();
        var displayIndex = normalized.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
        if (displayIndex < 0) return null;

        var tail = normalized[displayIndex..];
        var parts = tail.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        // Conservador: usa somente família/modelo expostos pelo device path. Monitores idênticos
        // continuam ambíguos e nunca são trocados silenciosamente pelo resolver.
        return $"{parts[0]}#{parts[1]}".ToUpperInvariant();
    }

    private static IDesktopWallpaperNative Create() => (IDesktopWallpaperNative)new DesktopWallpaperClass();

    private static void Release(object value)
    {
        if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static DesktopWallpaperPosition Map(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Center => DesktopWallpaperPosition.Center,
        WallpaperStyle.Tile => DesktopWallpaperPosition.Tile,
        WallpaperStyle.Stretch => DesktopWallpaperPosition.Stretch,
        WallpaperStyle.Fit => DesktopWallpaperPosition.Fit,
        WallpaperStyle.Span => DesktopWallpaperPosition.Span,
        _ => DesktopWallpaperPosition.Fill
    };
}
