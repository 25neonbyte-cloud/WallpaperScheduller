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
            var light = mode == SystemThemeMode.Light ? 1 : 0;
            if (_lastApplied == mode && RegistryMatches(light))
                return new(false, $"Tema {Describe(mode)} já confirmado no Windows.", mode);

            using var key = Registry.CurrentUser.CreateSubKey(PersonalizePath, writable: true)
                            ?? throw new InvalidOperationException("Não foi possível abrir as preferências de tema do Windows.");

            if (!_captured)
            {
                _originalApps = key.GetValue(AppsUseLightTheme);
                _originalSystem = key.GetValue(SystemUsesLightTheme);
                _captured = true;
            }

            key.SetValue(AppsUseLightTheme, light, RegistryValueKind.DWord);
            key.SetValue(SystemUsesLightTheme, light, RegistryValueKind.DWord);
            key.Flush();
            BroadcastThemeChange();

            var appsConfirmed = Convert.ToInt32(key.GetValue(AppsUseLightTheme, 1)) == light;
            var systemConfirmed = Convert.ToInt32(key.GetValue(SystemUsesLightTheme, 1)) == light;
            if (!appsConfirmed || !systemConfirmed)
            {
                logger.Warning($"O Windows não confirmou integralmente a gravação do tema {Describe(mode)}.");
                return new(false, $"Falha ao confirmar o tema {Describe(mode)} no Windows.", GetCurrentMode());
            }

            _lastApplied = mode;
            logger.Info($"Tema do sistema aplicado e confirmado no registro do Windows: {Describe(mode)}.");
            return new(true, $"Tema {Describe(mode)} aplicado e confirmado no Windows.", mode);
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
                key.Flush();
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

    private static bool RegistryMatches(int light)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath, writable: false);
        if (key is null) return false;
        return Convert.ToInt32(key.GetValue(AppsUseLightTheme, 1)) == light &&
               Convert.ToInt32(key.GetValue(SystemUsesLightTheme, 1)) == light;
    }

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
            500,
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
