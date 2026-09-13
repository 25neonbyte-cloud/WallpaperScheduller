namespace WallpaperScheduler.Domain;

public enum WallpaperStyle { Fill, Fit, Span, Center, Stretch, Tile }
public enum WallpaperScope { AllMonitors, PerMonitor }

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
    public string? Image { get; set; }
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SchedulerSettings
{
    public int HeartbeatSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public SchedulerSettings Scheduler { get; set; } = new();
    public List<WallpaperRule> Rules { get; set; } = [];
}

public sealed record RuleEvaluation(WallpaperRule? Winner, IReadOnlyList<WallpaperRule> Candidates, string Reason)
{
    public bool HasMatch => Winner is not null;
}
