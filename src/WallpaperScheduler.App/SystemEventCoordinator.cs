using System.Windows.Threading;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class SystemEventCoordinator : IDisposable
{
    private readonly ISystemEvents _systemEvents;
    private readonly WallpaperSchedulerHostedLoop _loop;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounceTimer;
    private bool _started;

    public SystemEventCoordinator(ISystemEvents systemEvents, WallpaperSchedulerHostedLoop loop)
    {
        _systemEvents = systemEvents;
        _loop = loop;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _debounceTimer.Tick += OnDebounceTick;
    }

    public void Start()
    {
        if (_started) return;

        _systemEvents.EventOccurred += OnSystemEventOccurred;
        try
        {
            _systemEvents.Start();
            _started = true;
        }
        catch
        {
            _systemEvents.EventOccurred -= OnSystemEventOccurred;
            throw;
        }
    }

    private void OnSystemEventOccurred(object? sender, SystemEventNotification e)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }));
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        await _loop.HandleSystemEventAsync();
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;

        if (_started)
        {
            _systemEvents.EventOccurred -= OnSystemEventOccurred;
            _started = false;
        }

        _systemEvents.Dispose();
    }
}
