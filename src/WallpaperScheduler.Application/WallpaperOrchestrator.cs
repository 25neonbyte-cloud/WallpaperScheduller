using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed class WallpaperOrchestrator(
    IConfigStore configStore,
    IRuleEngine ruleEngine,
    IMonitorService monitorService,
    IWallpaperApplier wallpaperApplier,
    IClock clock)
{
    private WallpaperState? _lastApplied;

    public async Task<ApplyResult> ApplyCurrentAsync(CancellationToken cancellationToken = default)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var evaluation = ruleEngine.Evaluate(config.Rules, clock.Now);
        if (!evaluation.HasMatch)
            return new(false, evaluation.Reason);

        var rule = evaluation.Winner!;
        var monitors = monitorService.GetActiveMonitors();
        var assignments = BuildAssignments(rule, monitors);
        if (assignments.Count == 0)
            return new(false, $"Regra '{rule.Name}' não possui imagem aplicável aos monitores ativos.");

        var state = new WallpaperState(rule.Id, rule.Style, assignments);
        if (EqualsState(_lastApplied, state))
            return new(false, "Estado desejado já está aplicado.", state);

        wallpaperApplier.Apply(state);
        _lastApplied = state;
        return new(true, evaluation.Reason, state);
    }

    private static List<WallpaperAssignment> BuildAssignments(WallpaperRule rule, IReadOnlyList<MonitorInfo> monitors)
    {
        var result = new List<WallpaperAssignment>();
        if (rule.Scope == WallpaperScope.AllMonitors)
        {
            if (string.IsNullOrWhiteSpace(rule.Image) || !File.Exists(rule.Image)) return result;
            result.AddRange(monitors.Select(m => new WallpaperAssignment(m.Id, rule.Image)));
            return result;
        }

        foreach (var monitor in monitors)
        {
            if (rule.PerMonitor.TryGetValue(monitor.Id, out var image) && File.Exists(image))
                result.Add(new(monitor.Id, image));
        }
        return result;
    }

    private static bool EqualsState(WallpaperState? a, WallpaperState b)
    {
        if (a is null || a.RuleId != b.RuleId || a.Style != b.Style || a.Assignments.Count != b.Assignments.Count) return false;
        return a.Assignments.OrderBy(x => x.MonitorId).SequenceEqual(b.Assignments.OrderBy(x => x.MonitorId));
    }
}
