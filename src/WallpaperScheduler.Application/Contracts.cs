using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed record MonitorInfo(
    string Id,
    string Name,
    int Width,
    int Height,
    string? HardwareKey = null,
    int Left = 0,
    int Top = 0);

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

public enum SystemEventKind
{
    DisplaySettingsChanged,
    Resume,
    SessionUnlocked,
    SessionLogon,
    TimeChanged
}

public sealed record SystemEventNotification(SystemEventKind Kind, DateTimeOffset OccurredAt);

public sealed record SystemThemeApplyResult(bool Applied, string Message, SystemThemeMode Mode);

public sealed record TemperatureMonitorRequest(
    Guid ProfileId,
    string ProfileName,
    MonitorInfo Monitor,
    int Kelvin,
    TemperatureApplicationMethod Method);

public sealed record TemperatureMonitorCapability(
    Guid ProfileId,
    string ProfileName,
    bool SoftwareAvailable,
    bool HdrActive,
    bool DdcCiAvailable,
    IReadOnlyList<int> DdcCiTemperatures,
    string Message);

public sealed record TemperatureMonitorApplyStatus(
    Guid ProfileId,
    string ProfileName,
    TemperatureApplicationMethod Method,
    int RequestedKelvin,
    int? AppliedKelvin,
    bool Applied,
    bool ConflictDetected,
    bool HdrBlocked,
    string Message);

public sealed record ColorTemperatureApplyResult(
    bool Applied,
    IReadOnlyList<TemperatureMonitorApplyStatus> Monitors,
    string Message);

public sealed record VisualComfortApplyResult(bool Applied, string Message);

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

public interface IConfigTransferService
{
    Task ExportAsync(AppConfig config, string destinationPath, CancellationToken cancellationToken = default);
    Task<AppConfig> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    string LogFilePath { get; }
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
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

public interface ISystemEvents : IDisposable
{
    event EventHandler<SystemEventNotification>? EventOccurred;
    void Start();
}

public interface ISystemThemeService : IDisposable
{
    SystemThemeMode GetCurrentMode();
    SystemThemeApplyResult Apply(SystemThemeMode mode);
    void Restore();
}

public interface IColorTemperatureService : IDisposable
{
    IReadOnlyList<TemperatureMonitorCapability> GetCapabilities(IReadOnlyList<MonitorResolution> resolutions);
    ColorTemperatureApplyResult Apply(IReadOnlyList<TemperatureMonitorRequest> requests, bool forceSoftwareConflict);
    void Restore();
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
