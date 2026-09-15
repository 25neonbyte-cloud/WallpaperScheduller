using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsStartupService : IStartupService
{
    private const string ShortcutName = "WallpaperScheduler.lnk";
    private const string StartupArgument = "--startup";
    private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "WallpaperScheduler";
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    public bool IsEnabled
    {
        get
        {
            var status = GetStatus();
            return status.Enabled && status.Valid;
        }
    }

    public StartupRegistrationStatus GetStatus()
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            if (!File.Exists(shortcutPath))
            {
                return HasLegacyRunEntry()
                    ? new(false, false, "Existe um registro legado de inicialização, mas o atalho atual do Startup não foi criado.", shortcutPath)
                    : new(false, true, "Inicialização automática desativada.", shortcutPath);
            }

            var executable = GetExecutablePath();
            var shortcut = ReadShortcut(shortcutPath);

            if (!PathsEqual(shortcut.TargetPath, executable))
                return new(true, false, $"O atalho de inicialização aponta para outro executável: {shortcut.TargetPath}", shortcutPath);

            if (!string.Equals(shortcut.Arguments.Trim(), StartupArgument, StringComparison.OrdinalIgnoreCase))
                return new(true, false, "O atalho de inicialização existe, mas os argumentos estão incorretos.", shortcutPath);

            if (!File.Exists(executable))
                return new(true, false, "O executável registrado para inicialização não existe mais.", shortcutPath);

            return new(true, true, "Inicialização automática registrada e validada no Startup do usuário.", shortcutPath);
        }
        catch (Exception ex)
        {
            return new(false, false, $"Falha ao verificar a inicialização automática: {ex.Message}", SafeShortcutPath());
        }
    }

    public void SetEnabled(bool enabled)
    {
        var shortcutPath = GetShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        // Remove o mecanismo antigo para evitar duas entradas concorrentes.
        RemoveLegacyRunEntry();

        if (!enabled)
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            if (File.Exists(shortcutPath))
                throw new InvalidOperationException("O atalho de inicialização não pôde ser removido.");
            return;
        }

        var executable = GetExecutablePath();
        CreateShortcut(shortcutPath, executable);

        var status = GetStatus();
        if (!status.Enabled || !status.Valid)
            throw new InvalidOperationException(status.Message);
    }

    private static string GetShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startupFolder))
            throw new InvalidOperationException("O Windows não informou a pasta Startup do usuário atual.");
        return Path.Combine(startupFolder, ShortcutName);
    }

    private static string? SafeShortcutPath()
    {
        try { return GetShortcutPath(); }
        catch { return null; }
    }

    private static string GetExecutablePath()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Não foi possível determinar o executável atual.");
        return Path.GetFullPath(executable);
    }

    private static void CreateShortcut(string shortcutPath, string executable)
    {
        var shellLink = CreateShellLink();
        try
        {
            shellLink.SetPath(executable);
            shellLink.SetArguments(StartupArgument);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory);
            shellLink.SetDescription("Wallpaper Scheduler");
            shellLink.SetIconLocation(executable, 0);

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, remember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static ShortcutInfo ReadShortcut(string shortcutPath)
    {
        var shellLink = CreateShellLink();
        try
        {
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(shortcutPath, 0);

            var target = new StringBuilder(32768);
            shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var arguments = new StringBuilder(2048);
            shellLink.GetArguments(arguments, arguments.Capacity);

            return new(target.ToString(), arguments.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static IShellLinkW CreateShellLink()
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClsid, throwOnError: true)
                   ?? throw new InvalidOperationException("Componente ShellLink do Windows indisponível.");
        return (IShellLinkW)(Activator.CreateInstance(type)
               ?? throw new InvalidOperationException("Não foi possível criar o atalho de inicialização."));
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: false);
        return key?.GetValue(LegacyValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    private sealed record ShortcutInfo(string TargetPath, string Arguments);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
