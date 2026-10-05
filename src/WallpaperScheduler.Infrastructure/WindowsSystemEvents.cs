using Microsoft.Win32;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsSystemEvents : ISystemEvents
{
    private bool _started;
    private bool _disposed;

    public event EventHandler<SystemEventNotification>? EventOccurred;

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsSystemEvents));
        if (_started) return;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.TimeChanged += OnTimeChanged;
        _started = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Publish(SystemEventKind.DisplaySettingsChanged);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Publish(SystemEventKind.Resume);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionUnlock:
                Publish(SystemEventKind.SessionUnlocked);
                break;
            case SessionSwitchReason.SessionLogon:
                Publish(SystemEventKind.SessionLogon);
                break;
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e) =>
        Publish(SystemEventKind.TimeChanged);

    private void Publish(SystemEventKind kind)
    {
        if (!_started || _disposed) return;
        EventOccurred?.Invoke(this, new SystemEventNotification(kind, DateTimeOffset.Now));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_started)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.TimeChanged -= OnTimeChanged;
            _started = false;
        }
    }
}
