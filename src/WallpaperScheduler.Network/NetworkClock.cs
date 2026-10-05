using WallpaperScheduler.Application;

namespace WallpaperScheduler.Network;

public sealed class NetworkClockState
{
    private long _offsetTicks;
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
    public void Update(DateTimeOffset controllerUtcNow)
    {
        var offset = controllerUtcNow - DateTimeOffset.UtcNow;
        if (Math.Abs(offset.TotalHours) > 12) return;
        Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
    }
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}

public sealed class NetworkSynchronizedClock(NetworkClockState state) : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now + state.Offset;
}
