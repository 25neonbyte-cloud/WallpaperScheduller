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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new ServiceCollection();
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperScheduler");
        var configPath = Path.Combine(basePath, "config.json");
        var bindingsPath = Path.Combine(basePath, "monitor-bindings.json");

        services.AddSingleton<IRuleEngine, RuleEngine>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IConfigStore>(_ => new JsonConfigStore(configPath));
        services.AddSingleton<IMonitorBindingStore>(_ => new JsonMonitorBindingStore(bindingsPath));
        services.AddSingleton<IMonitorProfileResolver, MonitorProfileResolver>();
        services.AddSingleton<WindowsWallpaperService>();
        services.AddSingleton<IMonitorService>(sp => sp.GetRequiredService<WindowsWallpaperService>());
        services.AddSingleton<IWallpaperApplier>(sp => sp.GetRequiredService<WindowsWallpaperService>());
        services.AddSingleton<WallpaperOrchestrator>();
        services.AddSingleton<WallpaperSchedulerHostedLoop>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();
        _services.GetRequiredService<MainWindow>().Show();
        _services.GetRequiredService<WallpaperSchedulerHostedLoop>().Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
