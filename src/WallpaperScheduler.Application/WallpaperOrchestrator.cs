using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed class WallpaperOrchestrator(
    IConfigStore configStore,
    IRuleEngine ruleEngine,
    IMonitorService monitorService,
    IWallpaperApplier wallpaperApplier,
    IClock clock)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private WallpaperState? _lastApplied;

    public async Task<ApplyResult> ApplyCurrentAsync(CancellationToken cancellationToken = default)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var evaluation = ruleEngine.Evaluate(config.Rules, clock.Now);
        if (!evaluation.HasMatch)
            return new(false, evaluation.Reason);

        var rule = evaluation.Winner!;
        var monitors = monitorService.GetActiveMonitors();
        var assignments = BuildAssignments(rule, monitors, clock.Now);
        if (assignments.Count == 0)
            return new(false, $"Regra '{rule.Name}' não possui imagem aplicável aos monitores ativos.");

        var state = new WallpaperState(rule.Id, rule.Style, assignments);
        if (EqualsState(_lastApplied, state))
            return new(false, "Estado desejado já está aplicado.", state);

        wallpaperApplier.Apply(state);
        _lastApplied = state;
        return new(true, evaluation.Reason, state);
    }

    private static List<WallpaperAssignment> BuildAssignments(
        WallpaperRule rule,
        IReadOnlyList<MonitorInfo> monitors,
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

        // Enquanto a camada de binding de perfis lógicos é concluída, mantém compatibilidade com schema v1.
        foreach (var monitor in monitors)
        {
            if (rule.PerMonitor.TryGetValue(monitor.Id, out var image) && File.Exists(image))
                result.Add(new(monitor.Id, image));
        }

        return result;
    }

    private static string? ResolveLegacyImage(string? image) =>
        !string.IsNullOrWhiteSpace(image) && File.Exists(image) ? image : null;

    private static string? ResolveSource(WallpaperRule rule, WallpaperSource? source, DateTimeOffset now)
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
            catch (UnauthorizedAccessException)
            {
                // Uma subpasta inacessível não invalida as demais fontes.
            }
            catch (IOException)
            {
                // Fonte temporariamente indisponível: ignora nesta avaliação.
            }
        }

        candidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var interval = rule.RotationIntervalMinutes;
        var slot = interval is > 0
            ? now.ToUnixTimeMinutes() / interval.Value
            : 0;

        if (rule.RotationMode == WallpaperRotationMode.Sequential)
            return candidates[(int)(Math.Abs(slot) % candidates.Count)];

        var seed = HashCode.Combine(rule.Id, slot);
        var random = new Random(seed);
        return candidates[random.Next(candidates.Count)];
    }

    private static bool IsSupportedFile(string path) =>
        File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    private static bool EqualsState(WallpaperState? a, WallpaperState b)
    {
        if (a is null || a.RuleId != b.RuleId || a.Style != b.Style || a.Assignments.Count != b.Assignments.Count) return false;
        return a.Assignments.OrderBy(x => x.MonitorId).SequenceEqual(b.Assignments.OrderBy(x => x.MonitorId));
    }
}

internal static class DateTimeOffsetExtensions
{
    public static long ToUnixTimeMinutes(this DateTimeOffset value) => value.ToUnixTimeSeconds() / 60;
}
