using Sms.Modules.Academics;

namespace Sms.Tests.Unit.Academics;

public class PeriodAttendanceDatePresetsTests
{
    static readonly DateOnly Today = new(2026, 8, 13);

    [Fact]
    public void Today_preset_is_single_day()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("today", null, null, Today);
        Assert.Equal(Today, from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Last_30_days_inclusive_of_today()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("last_30_days", null, null, Today);
        Assert.Equal(Today.AddDays(-29), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Explicit_from_to_wins_over_preset()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve(
            "today", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), Today);
        Assert.Equal(new DateOnly(2026, 7, 1), from);
        Assert.Equal(new DateOnly(2026, 7, 31), to);
    }

    [Fact]
    public void Yesterday_preset_is_single_day()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("yesterday", null, null, Today);
        Assert.Equal(Today.AddDays(-1), from);
        Assert.Equal(Today.AddDays(-1), to);
    }

    [Fact]
    public void This_week_is_monday_through_sunday()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("this_week", null, null, Today);
        Assert.Equal(new DateOnly(2026, 8, 10), from);
        Assert.Equal(new DateOnly(2026, 8, 16), to);
    }

    [Fact]
    public void Last_week_is_previous_monday_through_sunday()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("last_week", null, null, Today);
        Assert.Equal(new DateOnly(2026, 8, 3), from);
        Assert.Equal(new DateOnly(2026, 8, 9), to);
    }

    [Fact]
    public void This_month_starts_first_of_month_through_today()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("this_month", null, null, Today);
        Assert.Equal(new DateOnly(2026, 8, 1), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Last_month_is_full_previous_calendar_month()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("last_month", null, null, Today);
        Assert.Equal(new DateOnly(2026, 7, 1), from);
        Assert.Equal(new DateOnly(2026, 7, 31), to);
    }

    [Fact]
    public void Last_60_days_inclusive_of_today()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("last_60_days", null, null, Today);
        Assert.Equal(Today.AddDays(-59), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Last_90_days_inclusive_of_today()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve("last_90_days", null, null, Today);
        Assert.Equal(Today.AddDays(-89), from);
        Assert.Equal(Today, to);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_empty_preset_defaults_to_today(string? preset)
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve(preset, null, null, Today);
        Assert.Equal(Today, from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Only_from_provided_defaults_to_to_today()
    {
        var explicitFrom = new DateOnly(2026, 7, 1);
        var (from, to) = PeriodAttendanceDatePresets.Resolve(null, explicitFrom, null, Today);
        Assert.Equal(explicitFrom, from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Only_to_provided_defaults_from_to_today()
    {
        var explicitTo = new DateOnly(2026, 8, 20);
        var (from, to) = PeriodAttendanceDatePresets.Resolve(null, null, explicitTo, Today);
        Assert.Equal(Today, from);
        Assert.Equal(explicitTo, to);
    }

    [Fact]
    public void From_greater_than_to_is_clamped()
    {
        var (from, to) = PeriodAttendanceDatePresets.Resolve(
            null, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 10), Today);
        Assert.Equal(new DateOnly(2026, 8, 10), from);
        Assert.Equal(new DateOnly(2026, 8, 20), to);
    }
}
