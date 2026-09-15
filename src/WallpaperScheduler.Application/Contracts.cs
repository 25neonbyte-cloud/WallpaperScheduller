using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed record MonitorInfo(string Id, string Name, int Width, int Height, string? HardwareKey = null);
public sealed record WallpaperAssignment(string MonitorId, string ImagePath);
public sealed record WallpaperState(Guid RuleId, WallpaperStyle Style, IReadOnlyList<WallpaperAssignment> Assignments);
public sealed record ApplyResult(bool Applied, string Message, WallpaperState? State = null);

public sealed record MonitorBinding(
    Guid ProfileId,
    string? HardwareKey,
    string? LastKnownDevicePath,
    int? LastKnownWidth,
    int? LastKnownHeight);

public sealed record MonitorResolution(
    Guid ProfileId,
    string ProfileName,
    MonitorInfo? Monitor,
    bool IsAmbiguous,
    string Status);

public sealed record StartupRegistrationStatus(
    bool Enabled,
    bool Valid,
    string Message,
    string? RegistrationPath = null);

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

public interface IMonitorBindingStore
{
    Task<IReadOnlyList<MonitorBinding>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<MonitorBinding> bindings, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid profileId, CancellationToken cancellationToken = default);
}

public interface IMonitorProfileResolver
{
    Task<IReadOnlyList<MonitorResolution>> ResolveAsync(AppConfig config, IReadOnlyList<MonitorInfo> monitors, CancellationToken cancellationToken = default);
}

public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetActiveMonitors();
}

public interface IWallpaperApplier
{
    void Apply(WallpaperState state);
}

public interface IStartupService
{
    bool IsEnabled { get; }
    StartupRegistrationStatus GetStatus();
    void SetEnabled(bool enabled);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
