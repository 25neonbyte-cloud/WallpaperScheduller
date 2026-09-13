using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed record MonitorInfo(string Id, string Name, int Width, int Height);
public sealed record WallpaperAssignment(string MonitorId, string ImagePath);
public sealed record WallpaperState(Guid RuleId, WallpaperStyle Style, IReadOnlyList<WallpaperAssignment> Assignments);
public sealed record ApplyResult(bool Applied, string Message, WallpaperState? State = null);

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetActiveMonitors();
}

public interface IWallpaperApplier
{
    void Apply(WallpaperState state);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
