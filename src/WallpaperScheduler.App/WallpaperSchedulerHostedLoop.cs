using System.Windows.Threading;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class WallpaperSchedulerHostedLoop : IDisposable
{
    private readonly WallpaperOrchestrator _orchestrator;
    private readonly DispatcherTimer _timer;
    private bool _running;

    public WallpaperSchedulerHostedLoop(WallpaperOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
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
        if (_timer.IsEnabled) return;
        _timer.Start();
        _ = EvaluateAsync();
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        PauseStateChanged?.Invoke(this, EventArgs.Empty);
        if (!paused)
            _ = EvaluateAsync();
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
            if (forceReapply)
                await _orchestrator.ReapplyCurrentAsync();
            else
                await _orchestrator.ApplyCurrentAsync();
        }
        catch
        {
            // O loop não encerra por falha transitória; logging estruturado entra no hardening.
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
