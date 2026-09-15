namespace WallpaperScheduler.Domain;

public enum WallpaperStyle { Fill, Fit, Span, Center, Stretch, Tile }
public enum WallpaperScope { AllMonitors, PerMonitor }
public enum WallpaperSourceKind { File, Folder }
public enum WallpaperRotationMode { Sequential, Random }
public enum SystemThemeMode { Light, Dark }
public enum TemperatureApplicationMethod { Automatic, Software, DdcCi }
public enum VisualRoutineThemeTarget { Manual, Light, Dark }
public enum VisualControlMode { Manual, Scheduled }
public enum ApplicationThemeMode { FollowSystem, Light, Dark }

public sealed class WallpaperSourceItem
{
    public WallpaperSourceKind Kind { get; set; } = WallpaperSourceKind.File;
    public string Path { get; set; } = string.Empty;
}

public sealed class WallpaperSource
{
    public List<WallpaperSourceItem> Items { get; set; } = [];
    public bool IncludeSubfolders { get; set; }
}

public sealed class MonitorProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Monitor";
}

public sealed class WallpaperRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Nova regra";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public int Order { get; set; }
    public HashSet<DayOfWeek> DaysOfWeek { get; set; } = [];
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public WallpaperStyle Style { get; set; } = WallpaperStyle.Fill;
    public WallpaperScope Scope { get; set; } = WallpaperScope.AllMonitors;
    public WallpaperRotationMode RotationMode { get; set; } = WallpaperRotationMode.Sequential;
    public int? RotationIntervalMinutes { get; set; }

    // Compatibilidade com schema v1.
    public string? Image { get; set; }
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Schema v2: múltiplos arquivos/pastas e associação por perfil lógico de monitor.
    public WallpaperSource? Source { get; set; }
    public Dictionary<Guid, WallpaperSource> PerMonitorProfiles { get; set; } = [];
}

public sealed class SchedulerSettings
{
    public int HeartbeatSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; } = true;
}

public sealed class UiSettings
{
    public ApplicationThemeMode Theme { get; set; } = ApplicationThemeMode.FollowSystem;
}

public sealed class VisualComfortSettings
{
    public bool Enabled { get; set; }
    public SystemThemeSettings SystemTheme { get; set; } = new();
    public ColorTemperatureSettings Temperature { get; set; } = new();
    public VisualRoutineSettings Routine { get; set; } = new();
}

public sealed class SystemThemeSettings
{
    public bool Enabled { get; set; }
    public VisualControlMode ControlMode { get; set; } = VisualControlMode.Manual;
    public SystemThemeMode ManualMode { get; set; } = SystemThemeMode.Light;
    public TimeOnly LightStart { get; set; } = new(7, 0);
    public TimeOnly DarkStart { get; set; } = new(19, 0);
}

public sealed class ColorTemperatureSettings
{
    public bool Enabled { get; set; }
    public VisualControlMode ControlMode { get; set; } = VisualControlMode.Manual;
    public TemperatureApplicationMethod Method { get; set; } = TemperatureApplicationMethod.Automatic;
    public int ManualKelvin { get; set; } = 6500;
    public int DayKelvin { get; set; } = 6500;
    public int NightKelvin { get; set; } = 4200;
    public TimeOnly DayStart { get; set; } = new(7, 0);
    public TimeOnly NightStart { get; set; } = new(19, 0);
    public int TransitionMinutes { get; set; } = 30;
    public bool ForceSoftwareWhenExternalTransformDetected { get; set; }
    public Dictionary<Guid, TemperatureApplicationMethod> PerMonitorMethods { get; set; } = [];
}

public sealed class VisualRoutineSettings
{
    public bool Enabled { get; set; }
    public Dictionary<Guid, VisualRoutineBinding> Bindings { get; set; } = [];
}

public sealed class VisualRoutineBinding
{
    public VisualRoutineThemeTarget Theme { get; set; } = VisualRoutineThemeTarget.Manual;
    public int? TemperatureKelvin { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 4;
    public SchedulerSettings Scheduler { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public List<MonitorProfile> MonitorProfiles { get; set; } = [];
    public List<WallpaperRule> Rules { get; set; } = CreateDefaultCycle();
    public VisualComfortSettings VisualComfort { get; set; } = new();

    private static List<WallpaperRule> CreateDefaultCycle()
    {
        var everyDay = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>());
        return
        [
            Period("Após meia-noite", 0, 0, 5, 30, 0, everyDay),
            Period("Nascer do sol", 5, 30, 10, 0, 10, everyDay),
            Period("Dia claro", 10, 0, 16, 0, 20, everyDay),
            Period("Pôr do sol", 16, 0, 18, 45, 30, everyDay),
            Period("Noite", 18, 45, 0, 0, 40, everyDay)
        ];
    }

    private static WallpaperRule Period(
        string name,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        int order,
        HashSet<DayOfWeek> everyDay) => new()
    {
        Name = name,
        Priority = 100,
        Order = order,
        DaysOfWeek = new HashSet<DayOfWeek>(everyDay),
        Start = new(startHour, startMinute),
        End = new(endHour, endMinute),
        Source = new WallpaperSource()
    };
}

public sealed record RuleEvaluation(WallpaperRule? Winner, IReadOnlyList<WallpaperRule> Candidates, string Reason)
{
    public bool HasMatch => Winner is not null;
}
