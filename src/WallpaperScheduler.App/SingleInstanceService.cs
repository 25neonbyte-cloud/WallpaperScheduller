using System.Threading;
using System.Windows.Threading;

namespace WallpaperScheduler.App;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\WallpaperScheduler.SingleInstance";
    private const string ShowEventName = @"Local\WallpaperScheduler.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registeredWait;
    private bool _ownsMutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (_ownsMutex)
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        return _ownsMutex;
    }

    public static void SignalExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // A primeira instância ainda pode estar terminando sua inicialização.
        }
    }

    public void StartListening(Dispatcher dispatcher, Action showWindow)
    {
        if (!_ownsMutex || _showEvent is null || _registeredWait is not null) return;

        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, timedOut) =>
            {
                if (timedOut || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                dispatcher.BeginInvoke(DispatcherPriority.Normal, showWindow);
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _showEvent?.Dispose();
        _showEvent = null;

        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }

        _ownsMutex = false;
        _mutex?.Dispose();
        _mutex = null;
    }
}
