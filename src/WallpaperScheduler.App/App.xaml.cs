using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using WallpaperScheduler.Infrastructure;

namespace WallpaperScheduler.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private SingleInstanceService? _singleInstance;
    private IAppLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            SingleInstanceService.SignalExistingInstance();
            Shutdown();
            return;
        }

        var services = new ServiceCollection();
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperScheduler");
        var configPath = Path.Combine(basePath, "config.json");
        var bindingsPath = Path.Combine(basePath, "monitor-bindings.json");
        var logsPath = Path.Combine(basePath, "logs");

        services.AddSingleton<IAppLogger>(_ => new FileAppLogger(logsPath));
        services.AddSingleton<JsonConfigStore>(sp => new JsonConfigStore(configPath, sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton<IConfigStore>(sp => sp.GetRequiredService<JsonConfigStore>());
        services.AddSingleton<IConfigTransferService>(sp => sp.GetRequiredService<JsonConfigStore>());
        services.AddSingleton<IRuleEngine, RuleEngine>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IMonitorBindingStore>(_ => new JsonMonitorBindingStore(bindingsPath));
        services.AddSingleton<IMonitorProfileResolver, MonitorProfileResolver>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<ISystemEvents, WindowsSystemEvents>();
        services.AddSingleton<WindowsWallpaperService>();
        services.AddSingleton<IMonitorService>(sp => sp.GetRequiredService<WindowsWallpaperService>());
        services.AddSingleton<IWallpaperApplier>(sp => sp.GetRequiredService<WindowsWallpaperService>());
        services.AddSingleton<ISystemThemeService, WindowsSystemThemeService>();
        services.AddSingleton<IColorTemperatureService, WindowsColorTemperatureService>();
        services.AddSingleton<WallpaperOrchestrator>();
        services.AddSingleton<VisualComfortOrchestrator>();
        services.AddSingleton<WallpaperSchedulerHostedLoop>();
        services.AddSingleton<SystemEventCoordinator>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<TrayIconService>();

        _services = services.BuildServiceProvider();
        _logger = _services.GetRequiredService<IAppLogger>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _logger.Info("Wallpaper Scheduler iniciado.");
        DpiDiagnostics.LogProcess(_logger);

        var loop = _services.GetRequiredService<WallpaperSchedulerHostedLoop>();
        loop.Start();
        _services.GetRequiredService<SystemEventCoordinator>().Start();

        var startHidden = e.Args.Any(x => string.Equals(x, "--startup", StringComparison.OrdinalIgnoreCase));
        var tray = _services.GetRequiredService<TrayIconService>();
        tray.Start(startHidden);
        _singleInstance.StartListening(Dispatcher, tray.ShowWindow);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        _logger?.Error("Exceção não tratada na thread da interface.", e.Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _logger?.Error("Exceção não observada em tarefa assíncrona.", e.Exception);

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Wallpaper Scheduler encerrado.");
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _services?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
