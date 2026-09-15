using System.Runtime.InteropServices;
using Microsoft.Win32;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsSystemThemeService(IAppLogger logger) : ISystemThemeService
{
    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";
    private const uint WmSettingChange = 0x001A;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint SmtoAbortIfHung = 0x0002;

    private readonly object _gate = new();
    private bool _captured;
    private object? _originalApps;
    private object? _originalSystem;
    private SystemThemeMode? _lastApplied;

    public SystemThemeMode GetCurrentMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath, writable: false);
        var value = key?.GetValue(AppsUseLightTheme, 1);
        return Convert.ToInt32(value) == 0 ? SystemThemeMode.Dark : SystemThemeMode.Light;
    }

    public SystemThemeApplyResult Apply(SystemThemeMode mode)
    {
        lock (_gate)
        {
            if (_lastApplied == mode)
                return new(false, $"Tema {Describe(mode)} já aplicado.", mode);

            using var key = Registry.CurrentUser.CreateSubKey(PersonalizePath, writable: true)
                            ?? throw new InvalidOperationException("Não foi possível abrir as preferências de tema do Windows.");

            if (!_captured)
            {
                _originalApps = key.GetValue(AppsUseLightTheme);
                _originalSystem = key.GetValue(SystemUsesLightTheme);
                _captured = true;
            }

            var light = mode == SystemThemeMode.Light ? 1 : 0;
            key.SetValue(AppsUseLightTheme, light, RegistryValueKind.DWord);
            key.SetValue(SystemUsesLightTheme, light, RegistryValueKind.DWord);
            BroadcastThemeChange();

            _lastApplied = mode;
            logger.Info($"Tema do sistema aplicado: {Describe(mode)}.");
            return new(true, $"Tema {Describe(mode)} aplicado.", mode);
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            if (!_captured) return;

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizePath, writable: true);
                if (key is null) return;

                RestoreValue(key, AppsUseLightTheme, _originalApps);
                RestoreValue(key, SystemUsesLightTheme, _originalSystem);
                BroadcastThemeChange();
                logger.Info("Tema do sistema restaurado ao estado anterior ao Wallpaper Scheduler.");
            }
            catch (Exception ex)
            {
                logger.Error("Falha ao restaurar o tema anterior do Windows.", ex);
            }
            finally
            {
                _captured = false;
                _originalApps = null;
                _originalSystem = null;
                _lastApplied = null;
            }
        }
    }

    public void Dispose() => Restore();

    private static void RestoreValue(RegistryKey key, string name, object? value)
    {
        if (value is null)
        {
            try { key.DeleteValue(name, throwOnMissingValue: false); }
            catch { }
            return;
        }

        var kind = value switch
        {
            int => RegistryValueKind.DWord,
            long => RegistryValueKind.QWord,
            _ => RegistryValueKind.String
        };
        key.SetValue(name, value, kind);
    }

    private static void BroadcastThemeChange()
    {
        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            IntPtr.Zero,
            "ImmersiveColorSet",
            SmtoAbortIfHung,
            250,
            out _);
    }

    private static string Describe(SystemThemeMode mode) => mode == SystemThemeMode.Dark ? "escuro" : "claro";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
