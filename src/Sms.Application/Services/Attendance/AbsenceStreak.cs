namespace Sms.Application.Services.Attendance;

/// Pure trailing-consecutive-absence detection, mirroring the CRM's client-side logic.
/// Only *recorded* days count; gaps (unmarked days) are ignored, not treated as present.
/// A day is "absent" only if every mark recorded for that day is absent.
public static class AbsenceStreak
{
    public sealed record Flagged(Guid StudentId, int Streak, DateTime LastDate);

    public static IReadOnlyList<Flagged> Flag(
        IEnumerable<(Guid StudentId, DateTime Date, string Status)> marks, int minDays)
    {
        var threshold = minDays < 1 ? 1 : minDays;
        var flagged = new List<Flagged>();

        foreach (var group in marks.GroupBy(m => m.StudentId))
        {
            var days = group
                .GroupBy(m => m.Date.Date)
                .Select(d => new { Date = d.Key, Absent = d.All(x => IsAbsent(x.Status)) })
                .OrderByDescending(d => d.Date)
                .ToList();

            var streak = 0;
            var last = default(DateTime);
            foreach (var day in days)
            {
                if (!day.Absent) break;
                if (streak == 0) last = day.Date;
                streak++;
            }

            if (streak >= threshold) flagged.Add(new Flagged(group.Key, streak, last));
        }

        return flagged
            .OrderByDescending(f => f.Streak)
            .ThenByDescending(f => f.LastDate)
            .ToList();
    }

    private static bool IsAbsent(string? status) =>
        string.Equals((status ?? "").Trim(), "absent", StringComparison.OrdinalIgnoreCase);
}
