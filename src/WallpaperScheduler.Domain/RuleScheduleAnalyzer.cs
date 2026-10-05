namespace WallpaperScheduler.Domain;

public sealed record RuleConflict(Guid FirstRuleId, Guid SecondRuleId, Guid WinnerRuleId);

public static class RuleScheduleAnalyzer
{
    private const int MinutesPerDay = 24 * 60;
    private const int MinutesPerWeek = 7 * MinutesPerDay;

    public static IReadOnlyList<RuleConflict> FindConflicts(IEnumerable<WallpaperRule> rules)
    {
        var active = rules
            .Where(IsStructurallyValid)
            .OrderBy(r => r.Order)
            .ToList();

        var result = new List<RuleConflict>();
        for (var i = 0; i < active.Count; i++)
        {
            for (var j = i + 1; j < active.Count; j++)
            {
                var a = active[i];
                var b = active[j];
                if (!Overlaps(a, b)) continue;

                var winner = ComparePrecedence(a, b) <= 0 ? a : b;
                result.Add(new(a.Id, b.Id, winner.Id));
            }
        }

        return result;
    }

    private static bool IsStructurallyValid(WallpaperRule rule) =>
        rule.Enabled && rule.DaysOfWeek.Count > 0 && rule.Start != rule.End;

    private static int ComparePrecedence(WallpaperRule a, WallpaperRule b)
    {
        var priority = b.Priority.CompareTo(a.Priority);
        if (priority != 0) return priority;

        var order = b.Order.CompareTo(a.Order);
        if (order != 0) return order;

        return a.Id.CompareTo(b.Id);
    }

    private static bool Overlaps(WallpaperRule a, WallpaperRule b)
    {
        var aSegments = ToWeeklySegments(a);
        var bSegments = ToWeeklySegments(b);

        foreach (var x in aSegments)
            foreach (var y in bSegments)
                if (x.Start < y.End && y.Start < x.End)
                    return true;

        return false;
    }

    private static List<(int Start, int End)> ToWeeklySegments(WallpaperRule rule)
    {
        var result = new List<(int Start, int End)>();
        var startMinute = rule.Start.Hour * 60 + rule.Start.Minute;
        var endMinute = rule.End.Hour * 60 + rule.End.Minute;
        var duration = endMinute > startMinute
            ? endMinute - startMinute
            : MinutesPerDay - startMinute + endMinute;

        foreach (var day in rule.DaysOfWeek)
        {
            var start = ((int)day * MinutesPerDay) + startMinute;
            var end = start + duration;

            if (end <= MinutesPerWeek)
            {
                result.Add((start, end));
            }
            else
            {
                result.Add((start, MinutesPerWeek));
                result.Add((0, end - MinutesPerWeek));
            }
        }

        return result;
    }
}
