using System.ComponentModel;
using System.Drawing;
using System.Windows;
using WallpaperScheduler.Application;
using WinForms = System.Windows.Forms;

namespace WallpaperScheduler.App;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly WallpaperSchedulerHostedLoop _loop;
    private readonly IStartupService _startupService;
    private readonly IConfigStore _configStore;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private bool _allowClose;

    public TrayIconService(
        MainWindow window,
        WallpaperSchedulerHostedLoop loop,
        IStartupService startupService,
        IConfigStore configStore)
    {
        _window = window;
        _loop = loop;
        _startupService = startupService;
        _configStore = configStore;

        var menu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Abrir Wallpaper Scheduler", null, (_, _) => ShowWindow());
        var applyItem = new WinForms.ToolStripMenuItem("Aplicar agora", null, async (_, _) => await _loop.ApplyNowAsync());
        _pauseItem = new WinForms.ToolStripMenuItem("Pausar automação", null, (_, _) => TogglePause());
        _startupItem = new WinForms.ToolStripMenuItem("Iniciar com o Windows", null, async (_, _) => await ToggleStartupAsync())
        {
            Checked = SafeReadStartupState(),
            CheckOnClick = false
        };
        var exitItem = new WinForms.ToolStripMenuItem("Sair", null, (_, _) => ExitApplication());

        menu.Items.Add(openItem);
        menu.Items.Add(applyItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "Wallpaper Scheduler",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        _loop.PauseStateChanged += OnPauseStateChanged;
        _window.Closing += OnWindowClosing;
        UpdatePauseText();
    }

    public void Start(bool startHidden)
    {
        if (startHidden)
            _window.Hide();
        else
            ShowWindow();
    }

    private void ShowWindow()
    {
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        _window.Hide();
    }

    private void TogglePause()
    {
        _loop.SetPaused(!_loop.IsPaused);
        UpdatePauseText();
    }

    private void OnPauseStateChanged(object? sender, EventArgs e) => UpdatePauseText();

    private void UpdatePauseText()
    {
        _pauseItem.Text = _loop.IsPaused ? "Retomar automação" : "Pausar automação";
        _notifyIcon.Text = _loop.IsPaused ? "Wallpaper Scheduler — pausado" : "Wallpaper Scheduler";
    }

    private bool SafeReadStartupState()
    {
        try { return _startupService.IsEnabled; }
        catch { return false; }
    }

    private async Task ToggleStartupAsync()
    {
        var enabled = !_startupItem.Checked;
        try
        {
            _startupService.SetEnabled(enabled);
            _startupItem.Checked = enabled;

            var config = await _configStore.LoadAsync();
            config.Scheduler.StartWithWindows = enabled;
            await _configStore.SaveAsync(config);
        }
        catch
        {
            _startupItem.Checked = SafeReadStartupState();
        }
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _notifyIcon.Visible = false;
        _window.Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _loop.PauseStateChanged -= OnPauseStateChanged;
        _window.Closing -= OnWindowClosing;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
