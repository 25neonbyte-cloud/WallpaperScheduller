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
    private readonly IAppLogger _logger;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private bool _allowClose;

    public TrayIconService(
        MainWindow window,
        WallpaperSchedulerHostedLoop loop,
        IStartupService startupService,
        IConfigStore configStore,
        IAppLogger logger)
    {
        _window = window;
        _loop = loop;
        _startupService = startupService;
        _configStore = configStore;
        _logger = logger;

        var startupStatus = SafeReadStartupStatus();
        var menu = new WinForms.ContextMenuStrip { ShowItemToolTips = true };
        var openItem = new WinForms.ToolStripMenuItem("Abrir Wallpaper Scheduler", null, (_, _) => ShowWindow());
        var applyItem = new WinForms.ToolStripMenuItem("Aplicar agora", null, async (_, _) => await _loop.ApplyNowAsync());
        _pauseItem = new WinForms.ToolStripMenuItem("Pausar automação", null, (_, _) => TogglePause());
        _startupItem = new WinForms.ToolStripMenuItem("Iniciar com o Windows", null, async (_, _) => await ToggleStartupAsync())
        {
            Checked = startupStatus.Enabled && startupStatus.Valid,
            CheckOnClick = false,
            ToolTipText = startupStatus.Message
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

        _ = ReconcileStartupRegistrationAsync();
    }

    public void ShowWindow()
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

    private StartupRegistrationStatus SafeReadStartupStatus()
    {
        try { return _startupService.GetStatus(); }
        catch (Exception ex)
        {
            _logger.Error("Falha ao consultar registro de inicialização do Windows.", ex);
            return new(false, false, $"Falha ao verificar inicialização: {ex.Message}");
        }
    }

    private async Task ReconcileStartupRegistrationAsync()
    {
        try
        {
            var config = await _configStore.LoadAsync();
            var status = SafeReadStartupStatus();

            if (config.Scheduler.StartWithWindows)
            {
                if (!status.Enabled || !status.Valid)
                {
                    _startupService.SetEnabled(true);
                    status = _startupService.GetStatus();
                    _logger.Info("Registro de inicialização do Windows reconciliado.");
                }
            }
            else if (status.Enabled)
            {
                _startupService.SetEnabled(false);
                status = _startupService.GetStatus();
                _logger.Info("Registro de inicialização removido para respeitar a configuração do usuário.");
            }

            _startupItem.Checked = config.Scheduler.StartWithWindows && status.Enabled && status.Valid;
            _startupItem.ToolTipText = status.Message;

            if (config.Scheduler.StartWithWindows && !_startupItem.Checked)
                ShowStartupNotification("Inicialização não validada", status.Message, WinForms.ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = false;
            _startupItem.ToolTipText = ex.Message;
            _logger.Error("Falha ao reconciliar inicialização automática.", ex);
            ShowStartupNotification("Falha na inicialização automática", ex.Message, WinForms.ToolTipIcon.Error);
        }
    }

    private async Task ToggleStartupAsync()
    {
        var enabled = !_startupItem.Checked;
        try
        {
            _startupService.SetEnabled(enabled);
            var status = _startupService.GetStatus();
            _startupItem.Checked = enabled && status.Enabled && status.Valid;
            _startupItem.ToolTipText = status.Message;

            if (enabled && !_startupItem.Checked)
                throw new InvalidOperationException(status.Message);

            var config = await _configStore.LoadAsync();
            config.Scheduler.StartWithWindows = enabled;
            await _configStore.SaveAsync(config);
            _logger.Info(enabled ? "Inicialização com o Windows ativada." : "Inicialização com o Windows desativada.");

            ShowStartupNotification(
                enabled ? "Inicialização ativada" : "Inicialização desativada",
                status.Message,
                WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            var status = SafeReadStartupStatus();
            _startupItem.Checked = status.Enabled && status.Valid;
            _startupItem.ToolTipText = status.Message;
            _logger.Error("Falha ao alterar inicialização automática.", ex);
            ShowStartupNotification("Falha ao alterar inicialização", ex.Message, WinForms.ToolTipIcon.Error);
        }
    }

    private void ShowStartupNotification(string title, string text, WinForms.ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
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
