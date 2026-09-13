namespace WallpaperScheduler.Domain;

public enum WallpaperStyle { Fill, Fit, Span, Center, Stretch, Tile }
public enum WallpaperScope { AllMonitors, PerMonitor }
public enum WallpaperSourceKind { File, Folder }

public sealed class WallpaperSource
{
    public WallpaperSourceKind Kind { get; set; } = WallpaperSourceKind.File;
    public string Path { get; set; } = string.Empty;
}

public sealed class MonitorProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Monitor";
    public string? HardwareKey { get; set; }
    public string? LastKnownDevicePath { get; set; }
    public int? LastKnownWidth { get; set; }
    public int? LastKnownHeight { get; set; }
}

public sealed class DayPeriodProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Período";
    public bool Enabled { get; set; } = true;
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public WallpaperStyle Style { get; set; } = WallpaperStyle.Fill;
    public WallpaperSource Source { get; set; } = new();
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

    // Compatibilidade com schema v1.
    public string? Image { get; set; }
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Schema v2: fontes abstratas e associação por perfil lógico, sem expor device path ao usuário.
    public WallpaperSource? Source { get; set; }
    public Dictionary<Guid, WallpaperSource> PerMonitorProfiles { get; set; } = [];
}

public sealed class SchedulerSettings
{
    public int HeartbeatSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 2;
    public SchedulerSettings Scheduler { get; set; } = new();
    public List<MonitorProfile> MonitorProfiles { get; set; } = [];
    public List<DayPeriodProfile> DayPeriods { get; set; } = CreateDefaultDayPeriods();
    public List<WallpaperRule> Rules { get; set; } = [];

    private static List<DayPeriodProfile> CreateDefaultDayPeriods() =>
    [
        new() { Name = "Após meia-noite", Start = new(0, 0), End = new(5, 30) },
        new() { Name = "Nascer do sol", Start = new(5, 30), End = new(10, 0) },
        new() { Name = "Dia claro", Start = new(10, 0), End = new(16, 0) },
        new() { Name = "Pôr do sol", Start = new(16, 0), End = new(18, 45) },
        new() { Name = "Noite", Start = new(18, 45), End = new(0, 0) }
    ];
}

public sealed record RuleEvaluation(WallpaperRule? Winner, IReadOnlyList<WallpaperRule> Candidates, string Reason)
{
    public bool HasMatch => Winner is not null;
}
