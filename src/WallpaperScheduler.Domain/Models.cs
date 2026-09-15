namespace WallpaperScheduler.Domain;

public enum WallpaperStyle { Fill, Fit, Span, Center, Stretch, Tile }
public enum WallpaperScope { AllMonitors, PerMonitor }
public enum WallpaperSourceKind { File, Folder }
public enum WallpaperRotationMode { Sequential, Random }

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

public sealed class AppConfig
{
    public int Version { get; set; } = 2;
    public SchedulerSettings Scheduler { get; set; } = new();
    public List<MonitorProfile> MonitorProfiles { get; set; } = [];
    public List<WallpaperRule> Rules { get; set; } = CreateDefaultCycle();

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
