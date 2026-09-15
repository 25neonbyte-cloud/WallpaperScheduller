using System.Runtime.InteropServices;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsWallpaperService : IMonitorService, IWallpaperApplier
{
    public IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        // Topologia de vídeo pode mudar durante a enumeração. Uma falha COM em um
        // monitor que acabou de desaparecer não deve abortar a leitura dos demais.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var api = Create();
            try
            {
                try
                {
                    api.GetMonitorDevicePathCount(out var count);
                    var monitors = new List<MonitorInfo>((int)count);

                    for (uint i = 0; i < count; i++)
                    {
                        try
                        {
                            api.GetMonitorDevicePathAt(i, out var id);
                            api.GetMonitorRECT(id, out var rect);

                            var width = rect.Right - rect.Left;
                            var height = rect.Bottom - rect.Top;
                            if (width <= 0 || height <= 0) continue;

                            monitors.Add(new(
                                id,
                                $"Monitor {monitors.Count + 1}",
                                width,
                                height,
                                ExtractHardwareKey(id)));
                        }
                        catch (COMException)
                        {
                            // O índice/path ficou inválido após uma mudança de topologia.
                            // Ignora apenas este monitor e preserva os demais.
                        }
                    }

                    return monitors;
                }
                catch (COMException) when (attempt == 0)
                {
                    // A própria coleção mudou antes de conseguirmos lê-la. Recria o
                    // COM uma vez e tenta novamente com a topologia atual.
                }
            }
            finally
            {
                Release(api);
            }
        }

        return [];
    }

    public void Apply(WallpaperState state)
    {
        var api = Create();
        try
        {
            api.SetPosition(Map(state.Style));
            foreach (var assignment in state.Assignments)
            {
                try
                {
                    api.SetWallpaper(assignment.MonitorId, assignment.ImagePath);
                }
                catch (COMException)
                {
                    // Monitor desconectado entre avaliação e aplicação: os demais
                    // continuam sendo processados e a próxima reconciliação corrige o estado.
                }
            }
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
