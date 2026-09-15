using System.Windows.Threading;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class WallpaperSchedulerHostedLoop : IDisposable
{
    private readonly WallpaperOrchestrator _wallpaperOrchestrator;
    private readonly VisualComfortOrchestrator _comfortOrchestrator;
    private readonly IConfigStore _configStore;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _running;
    private bool _started;

    public WallpaperSchedulerHostedLoop(
        WallpaperOrchestrator wallpaperOrchestrator,
        VisualComfortOrchestrator comfortOrchestrator,
        IConfigStore configStore,
        IAppLogger logger)
    {
        _wallpaperOrchestrator = wallpaperOrchestrator;
        _comfortOrchestrator = comfortOrchestrator;
        _configStore = configStore;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += OnTick;
    }

    public bool IsPaused { get; private set; }

    public event EventHandler? PauseStateChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        if (!IsPaused) _timer.Start();
        _ = EvaluateAsync();
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;

        if (paused)
        {
            _timer.Stop();
            _logger.Info("Automação pausada pelo usuário.");
        }
        else
        {
            if (_started && !_timer.IsEnabled) _timer.Start();
            _logger.Info("Automação retomada pelo usuário.");
            _ = EvaluateAsync();
        }

        PauseStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ApplyNowAsync() => EvaluateAsync(ignorePause: true, forceReapply: true);

    public Task HandleSystemEventAsync() => EvaluateAsync(ignorePause: false, forceReapply: true);

    private async void OnTick(object? sender, EventArgs e) => await EvaluateAsync();

    private async Task EvaluateAsync(bool ignorePause = false, bool forceReapply = false)
    {
        if (_running || (IsPaused && !ignorePause)) return;
        try
        {
            _running = true;
            var config = await _configStore.LoadAsync();
            var heartbeat = Math.Clamp(config.Scheduler.HeartbeatSeconds, 10, 3600);
            var interval = TimeSpan.FromSeconds(heartbeat);
            if (_timer.Interval != interval) _timer.Interval = interval;

            if (forceReapply)
            {
                await _wallpaperOrchestrator.ReapplyCurrentAsync();
                await _comfortOrchestrator.ReapplyCurrentAsync();
            }
            else
            {
                await _wallpaperOrchestrator.ApplyCurrentAsync();
                await _comfortOrchestrator.ApplyCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Falha durante a avaliação/aplicação automática do ambiente visual.", ex);
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        _started = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _comfortOrchestrator.Restore();
    }
}
