using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

internal static class DpiDiagnostics
{
    private const int WmDpiChanged = 0x02E0;

    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);
    private static readonly IntPtr DpiAwarenessContextSystemAware = new(-2);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAware = new(-3);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private static readonly IntPtr DpiAwarenessContextUnawareGdiScaled = new(-5);

    public static void LogProcess(IAppLogger logger)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var hresult = GetProcessDpiAwareness(process.Handle, out var processAwareness);
            var threadContext = GetThreadDpiAwarenessContext();

            logger.Info(
                $"DPI process: awareness={processAwareness}; " +
                $"GetProcessDpiAwareness HRESULT=0x{hresult:X8}; " +
                $"threadContext={DescribeContext(threadContext)}.");
        }
        catch (Exception ex)
        {
            logger.Warning($"DPI process diagnostics failed: {ex.Message}");
        }
    }

    public static void Attach(Window window, IAppLogger logger)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            if (source is null)
            {
                logger.Warning("DPI diagnostics: HwndSource unavailable at SourceInitialized.");
                return;
            }

            source.AddHook((IntPtr hookHwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == WmDpiChanged)
                {
                    var packed = unchecked((ulong)wParam.ToInt64());
                    var dpiX = (ushort)(packed & 0xFFFF);
                    var dpiY = (ushort)((packed >> 16) & 0xFFFF);
                    logger.Info($"DPI WM_DPICHANGED received: requested={dpiX}x{dpiY}.");

                    window.Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(() => LogWindow(window, logger, "WM_DPICHANGED/applied")));
                }

                return IntPtr.Zero;
            });

            LogWindow(window, logger, "SourceInitialized");
        }
        catch (Exception ex)
        {
            logger.Warning($"DPI window diagnostics attach failed: {ex.Message}");
        }
    }

    public static void LogWindow(Window window, IAppLogger logger, string stage)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var windowContext = GetWindowDpiAwarenessContext(hwnd);
            var nativeDpi = GetDpiForWindow(hwnd);
            var wpfDpi = VisualTreeHelper.GetDpi(window);
            var source = HwndSource.FromHwnd(hwnd);
            var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

            logger.Info(
                $"DPI window [{stage}]: hwnd=0x{hwnd.ToInt64():X}; " +
                $"context={DescribeContext(windowContext)}; " +
                $"GetDpiForWindow={nativeDpi}; " +
                $"WPF dpi={wpfDpi.PixelsPerInchX:0.##}x{wpfDpi.PixelsPerInchY:0.##}; " +
                $"WPF scale={wpfDpi.DpiScaleX:0.###}x{wpfDpi.DpiScaleY:0.###}; " +
                $"TransformToDevice={toDevice.M11:0.###}x{toDevice.M22:0.###}; " +
                $"TransformFromDevice={fromDevice.M11:0.###}x{fromDevice.M22:0.###}; " +
                $"Window={window.Left:0.##},{window.Top:0.##} {window.Width:0.##}x{window.Height:0.##}.");
        }
        catch (Exception ex)
        {
            logger.Warning($"DPI window diagnostics failed at {stage}: {ex.Message}");
        }
    }

    private static string DescribeContext(IntPtr context)
    {
        if (context == IntPtr.Zero) return "NULL";
        if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAwareV2)) return "PerMonitorV2";
        if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAware)) return "PerMonitor";
        if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextSystemAware)) return "SystemAware";
        if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextUnawareGdiScaled)) return "UnawareGdiScaled";
        if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextUnaware)) return "Unaware";
        return $"Unknown(0x{context.ToInt64():X})";
    }

    private enum ProcessDpiAwareness
    {
        Unaware = 0,
        SystemAware = 1,
        PerMonitorAware = 2
    }

    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr hProcess, out ProcessDpiAwareness value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr dpiContextA, IntPtr dpiContextB);
}
