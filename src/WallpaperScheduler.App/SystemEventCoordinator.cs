using System.Windows.Threading;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class SystemEventCoordinator : IDisposable
{
    private readonly ISystemEvents _systemEvents;
    private readonly WallpaperSchedulerHostedLoop _loop;
    private readonly IAppLogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounceTimer;
    private bool _started;
    private SystemEventKind? _pendingKind;

    public SystemEventCoordinator(ISystemEvents systemEvents, WallpaperSchedulerHostedLoop loop, IAppLogger logger)
    {
        _systemEvents = systemEvents;
        _loop = loop;
        _logger = logger;
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
            _logger.Info("Monitoramento de eventos do Windows iniciado.");
        }
        catch (Exception ex)
        {
            _systemEvents.EventOccurred -= OnSystemEventOccurred;
            _logger.Error("Falha ao iniciar monitoramento de eventos do Windows.", ex);
            throw;
        }
    }

    private void OnSystemEventOccurred(object? sender, SystemEventNotification e)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _pendingKind = e.Kind;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }));
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (_pendingKind is { } kind)
            _logger.Info($"Evento do sistema recebido: {kind}.");
        _pendingKind = null;
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
