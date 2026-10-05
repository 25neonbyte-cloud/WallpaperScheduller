using WallpaperScheduler.Domain;
using Xunit;

namespace WallpaperScheduler.Domain.Tests;

public sealed class RuleEngineTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void Normal_interval_matches_inside_window()
    {
        var rule = Rule(DayOfWeek.Monday, "06:00", "12:00");
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 14, 8, 30)));
        Assert.False(_engine.IsMatch(rule, At(2026, 9, 14, 12, 0)));
    }

    [Fact]
    public void Overnight_rule_uses_start_day_semantics()
    {
        var rule = Rule(DayOfWeek.Sunday, "18:00", "06:00");
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 13, 23, 0)));
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 14, 2, 0)));
        Assert.False(_engine.IsMatch(rule, At(2026, 9, 14, 6, 0)));
    }

    [Fact]
    public void Overnight_1843_to_1300_stays_active_until_next_day_end()
    {
        var rule = Rule(DayOfWeek.Sunday, "18:43", "13:00");
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 13, 18, 43)));
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 14, 0, 0)));
        Assert.True(_engine.IsMatch(rule, At(2026, 9, 14, 12, 59)));
        Assert.False(_engine.IsMatch(rule, At(2026, 9, 14, 13, 0)));
    }

    [Fact]
    public void Highest_priority_wins_then_latest_order()
    {
        var low = Rule(DayOfWeek.Monday, "06:00", "12:00", priority: 100, order: 0);
        var highEarlier = Rule(DayOfWeek.Monday, "06:00", "12:00", priority: 200, order: 10);
        var highLatest = Rule(DayOfWeek.Monday, "06:00", "12:00", priority: 200, order: 20);
        var result = _engine.Evaluate([low, highEarlier, highLatest], At(2026, 9, 14, 9, 0));
        Assert.Same(highLatest, result.Winner);
    }

    [Fact]
    public void Later_overlapping_period_overrides_earlier_period_at_same_priority()
    {
        var earlier = Rule(DayOfWeek.Monday, "18:00", "23:00", order: 10);
        var later = Rule(DayOfWeek.Monday, "18:43", "13:00", order: 20);
        var result = _engine.Evaluate([earlier, later], At(2026, 9, 14, 19, 0));
        Assert.Same(later, result.Winner);
    }

    [Fact]
    public void Disabled_and_invalid_rules_are_ignored()
    {
        var disabled = Rule(DayOfWeek.Monday, "06:00", "12:00");
        disabled.Enabled = false;
        var invalid = Rule(DayOfWeek.Monday, "06:00", "06:00");
        var result = _engine.Evaluate([disabled, invalid], At(2026, 9, 14, 9, 0));
        Assert.False(result.HasMatch);
    }

    [Fact]
    public void Conflict_analyzer_detects_overnight_overlap_and_reports_latest_winner()
    {
        var earlier = Rule(DayOfWeek.Monday, "18:00", "23:00", order: 10);
        var later = Rule(DayOfWeek.Monday, "18:43", "13:00", order: 20);
        var conflicts = RuleScheduleAnalyzer.FindConflicts([earlier, later]);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(later.Id, conflict.WinnerRuleId);
    }

    private static WallpaperRule Rule(DayOfWeek day, string start, string end, int priority = 100, int order = 0) => new()
    {
        Name = "Teste", DaysOfWeek = [day], Start = TimeOnly.Parse(start), End = TimeOnly.Parse(end), Priority = priority, Order = order
    };

    private static DateTimeOffset At(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.Zero);
}
