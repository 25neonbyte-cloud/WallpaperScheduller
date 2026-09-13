namespace WallpaperScheduler.Domain;

public interface IRuleEngine
{
    RuleEvaluation Evaluate(IEnumerable<WallpaperRule> rules, DateTimeOffset now);
    bool IsMatch(WallpaperRule rule, DateTimeOffset now);
}

public sealed class RuleEngine : IRuleEngine
{
    public RuleEvaluation Evaluate(IEnumerable<WallpaperRule> rules, DateTimeOffset now)
    {
        var candidates = rules
            .Where(r => r.Enabled && IsStructurallyValid(r) && IsMatch(r, now))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Order)
            .ThenBy(r => r.Id)
            .ToList();

        if (candidates.Count == 0)
            return new(null, candidates, "Nenhuma regra corresponde ao momento atual.");

        var winner = candidates[0];
        return new(winner, candidates, $"Regra '{winner.Name}' venceu por priority DESC, order DESC, id ASC.");
    }

    public bool IsMatch(WallpaperRule rule, DateTimeOffset now)
    {
        if (!rule.Enabled || !IsStructurallyValid(rule)) return false;

        var time = TimeOnly.FromDateTime(now.LocalDateTime);
        var day = now.LocalDateTime.DayOfWeek;
        var overnight = rule.End < rule.Start;

        if (!overnight)
            return rule.DaysOfWeek.Contains(day) && time >= rule.Start && time < rule.End;

        if (time >= rule.Start)
            return rule.DaysOfWeek.Contains(day);

        if (time < rule.End)
            return rule.DaysOfWeek.Contains(Previous(day));

        return false;
    }

    private static bool IsStructurallyValid(WallpaperRule rule) =>
        rule.DaysOfWeek.Count > 0 && rule.Start != rule.End;

    private static DayOfWeek Previous(DayOfWeek day) =>
        day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;
}
