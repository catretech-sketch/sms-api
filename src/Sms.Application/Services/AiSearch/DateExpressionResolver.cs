namespace Sms.Application.Services.AiSearch;

public static class DateExpressionResolver
{
    public static (DateOnly From, DateOnly To) Resolve(string? expression, DateOnly today)
    {
        var expr = (expression ?? "today").Trim().ToLowerInvariant();
        return expr switch
        {
            "today" or "aaj" or "आज" => (today, today),
            "yesterday" or "kal" or "कल" => (today.AddDays(-1), today.AddDays(-1)),
            "this week" or "is week" or "इस हफ्ते" => (StartOfWeek(today), today),
            "last week" or "pichle week" or "पिछले हफ्ते" =>
                (StartOfWeek(today).AddDays(-7), StartOfWeek(today).AddDays(-1)),
            "this month" or "is month" or "इस महीने" => (new DateOnly(today.Year, today.Month, 1), today),
            _ => (today, today)
        };
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.AddDays(-diff);
    }
}
