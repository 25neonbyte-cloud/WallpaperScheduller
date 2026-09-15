using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed class WallpaperOrchestrator(
    IConfigStore configStore,
    IRuleEngine ruleEngine,
    IMonitorService monitorService,
    IMonitorProfileResolver monitorProfileResolver,
    IWallpaperApplier wallpaperApplier,
    IClock clock)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private WallpaperState? _lastApplied;

    public Task<ApplyResult> ApplyCurrentAsync(CancellationToken cancellationToken = default) =>
        ApplyCurrentCoreAsync(forceReapply: false, cancellationToken);

    public Task<ApplyResult> ReapplyCurrentAsync(CancellationToken cancellationToken = default) =>
        ApplyCurrentCoreAsync(forceReapply: true, cancellationToken);

    private async Task<ApplyResult> ApplyCurrentCoreAsync(bool forceReapply, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var evaluation = ruleEngine.Evaluate(config.Rules, clock.Now);
        if (!evaluation.HasMatch)
            return new(false, evaluation.Reason);

        var rule = evaluation.Winner!;
        var monitors = monitorService.GetActiveMonitors();
        var resolutions = await monitorProfileResolver.ResolveAsync(config, monitors, cancellationToken);
        var assignments = BuildAssignments(rule, monitors, resolutions, clock.Now);
        if (assignments.Count == 0)
            return new(false, $"Regra '{rule.Name}' não possui imagem aplicável aos monitores ativos.");

        var state = new WallpaperState(rule.Id, rule.Style, assignments);
        if (!forceReapply && EqualsState(_lastApplied, state))
            return new(false, evaluation.Reason + " Estado desejado já está aplicado.", state);

        wallpaperApplier.Apply(state);
        _lastApplied = state;
        return new(true, evaluation.Reason, state);
    }

    private static List<WallpaperAssignment> BuildAssignments(
        WallpaperRule rule,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<MonitorResolution> resolutions,
        DateTimeOffset now)
    {
        var result = new List<WallpaperAssignment>();

        if (rule.Scope == WallpaperScope.AllMonitors)
        {
            var image = ResolveSource(rule, rule.Source, now)
                        ?? ResolveLegacyImage(rule.Image);
            if (image is null) return result;

            result.AddRange(monitors.Select(m => new WallpaperAssignment(m.Id, image)));
            return result;
        }

        foreach (var resolution in resolutions)
        {
            if (resolution.Monitor is null || resolution.IsAmbiguous) continue;

            if (rule.PerMonitorProfiles.TryGetValue(resolution.ProfileId, out var source))
            {
                var image = ResolveSource(rule, source, now, resolution.ProfileId);
                if (image is not null)
                    result.Add(new(resolution.Monitor.Id, image));
                continue;
            }

            // Compatibilidade com schema v1 durante migração.
            if (rule.PerMonitor.TryGetValue(resolution.Monitor.Id, out var legacy) && File.Exists(legacy))
                result.Add(new(resolution.Monitor.Id, legacy));
        }

        return result;
    }

    private static string? ResolveLegacyImage(string? image) =>
        !string.IsNullOrWhiteSpace(image) && File.Exists(image) ? image : null;

    private static string? ResolveSource(
        WallpaperRule rule,
        WallpaperSource? source,
        DateTimeOffset now,
        Guid? profileId = null)
    {
        if (source is null || source.Items.Count == 0) return null;

        var candidates = new List<string>();
        foreach (var item in source.Items)
        {
            if (item.Kind == WallpaperSourceKind.File)
            {
                if (IsSupportedFile(item.Path)) candidates.Add(item.Path);
                continue;
            }

            if (!Directory.Exists(item.Path)) continue;
            var option = source.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(item.Path, "*.*", option).Where(IsSupportedFile));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        candidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var interval = rule.RotationIntervalMinutes;
        if (interval is not > 0) return candidates[0];

        var elapsedMinutes = GetElapsedMinutesSinceRuleStart(rule, now);
        var slot = Math.Max(0, elapsedMinutes / interval.Value);

        if (rule.RotationMode == WallpaperRotationMode.Sequential)
            return candidates[(int)(slot % candidates.Count)];

        var seed = HashCode.Combine(rule.Id, profileId, slot);
        var random = new Random(seed);
        return candidates[random.Next(candidates.Count)];
    }

    private static long GetElapsedMinutesSinceRuleStart(WallpaperRule rule, DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var time = TimeOnly.FromDateTime(local);
        var startDate = local.Date;

        if (rule.End < rule.Start && time < rule.End)
            startDate = startDate.AddDays(-1);

        var start = new DateTime(
            startDate.Year,
            startDate.Month,
            startDate.Day,
            rule.Start.Hour,
            rule.Start.Minute,
            0,
            local.Kind);

        return Math.Max(0, (long)Math.Floor((local - start).TotalMinutes));
    }

    private static bool IsSupportedFile(string path) =>
        File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    private static bool EqualsState(WallpaperState? a, WallpaperState b)
    {
        if (a is null || a.RuleId != b.RuleId || a.Style != b.Style || a.Assignments.Count != b.Assignments.Count) return false;
        return a.Assignments.OrderBy(x => x.MonitorId).SequenceEqual(b.Assignments.OrderBy(x => x.MonitorId));
    }
}
