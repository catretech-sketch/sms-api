namespace Sms.Modules.Academics;

/// <summary>
/// Resolves attendance CRM date presets and explicit bounds to an inclusive from/to range.
/// Week boundaries are Monday–Sunday relative to <paramref name="today"/>.
/// </summary>
public static class PeriodAttendanceDatePresets
{
    public static (DateOnly From, DateOnly To) Resolve(
        string? preset,
        DateOnly? from,
        DateOnly? to,
        DateOnly today)
    {
        DateOnly resolvedFrom;
        DateOnly resolvedTo;

        if (from.HasValue && to.HasValue)
        {
            resolvedFrom = from.Value;
            resolvedTo = to.Value;
        }
        else if (from.HasValue || to.HasValue)
        {
            resolvedFrom = from ?? today;
            resolvedTo = to ?? today;
        }
        else if (!string.IsNullOrWhiteSpace(preset))
        {
            (resolvedFrom, resolvedTo) = ResolvePreset(preset.Trim(), today);
        }
        else
        {
            resolvedFrom = today;
            resolvedTo = today;
        }

        if (resolvedFrom > resolvedTo)
            (resolvedFrom, resolvedTo) = (resolvedTo, resolvedFrom);

        return (resolvedFrom, resolvedTo);
    }

    static (DateOnly From, DateOnly To) ResolvePreset(string preset, DateOnly today)
    {
        return preset.ToLowerInvariant() switch
        {
            "today" => (today, today),
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "this_week" => WeekContaining(today),
            "last_week" => ShiftWeek(WeekContaining(today), -7),
            "this_month" => (new DateOnly(today.Year, today.Month, 1), today),
            "last_month" => PreviousCalendarMonth(today),
            "last_30_days" => (today.AddDays(-29), today),
            "last_60_days" => (today.AddDays(-59), today),
            "last_90_days" => (today.AddDays(-89), today),
            _ => (today, today),
        };
    }

    static (DateOnly From, DateOnly To) WeekContaining(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = date.AddDays(-daysFromMonday);
        return (monday, monday.AddDays(6));
    }

    static (DateOnly From, DateOnly To) ShiftWeek((DateOnly From, DateOnly To) week, int days)
        => (week.From.AddDays(days), week.To.AddDays(days));

    static (DateOnly From, DateOnly To) PreviousCalendarMonth(DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var lastOfPreviousMonth = firstOfThisMonth.AddDays(-1);
        var firstOfPreviousMonth = new DateOnly(lastOfPreviousMonth.Year, lastOfPreviousMonth.Month, 1);
        return (firstOfPreviousMonth, lastOfPreviousMonth);
    }
}
