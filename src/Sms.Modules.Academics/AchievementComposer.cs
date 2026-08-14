using Sms.Modules.Academics.Contracts;

namespace Sms.Modules.Academics;

/// <summary>
/// Builds the student Profile achievement list from live facts plus staff awards.
/// Computed badges are deterministic (stable ids) so the app can refresh without flicker.
/// </summary>
public static class AchievementComposer
{
    public static IReadOnlyList<AchievementResponse> Compose(
        Guid studentId,
        decimal? attendancePct,
        IReadOnlyList<string> homeworkStatuses,
        IReadOnlyList<(decimal Marks, decimal MaxMarks)> publishedGrades,
        IReadOnlyList<AchievementResponse> awarded,
        DateTime asOf)
    {
        var day = DateTime.SpecifyKind(asOf.Date, DateTimeKind.Unspecified);
        var list = new List<AchievementResponse>();

        if (attendancePct is >= 100m)
        {
            list.Add(Computed(studentId, "attendance-perfect", "Perfect attendance", day, "check", "teal"));
        }
        else if (attendancePct is >= 95m)
        {
            list.Add(Computed(studentId, "attendance-excellent", "Excellent attendance", day, "check", "teal"));
        }

        var hw = homeworkStatuses.Count;
        var done = homeworkStatuses.Count(s =>
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            return v is "submitted" or "graded";
        });
        if (hw > 0 && done == hw)
        {
            list.Add(Computed(studentId, "homework-complete", "All homework submitted", day, "star", "yellow"));
        }

        var scored = publishedGrades.Where(g => g.MaxMarks > 0).ToList();
        if (scored.Count > 0)
        {
            var avg = scored.Average(g => 100m * g.Marks / g.MaxMarks);
            if (avg >= 90m)
                list.Add(Computed(studentId, "academic-distinction", "Academic distinction", day, "award", "yellow"));
            else if (avg >= 80m)
                list.Add(Computed(studentId, "honor-roll", "Honor roll", day, "award", "blue"));
        }

        foreach (var a in awarded)
            list.Add(a);

        return list
            .OrderByDescending(a => a.Date)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static AchievementResponse FromAward(AchievementAwardRow row) =>
        new(row.Id.ToString(), row.Title, row.AwardedOn.Date, NormIcon(row.Icon), NormHue(row.Hue));

    public static string NormIcon(string? icon)
    {
        var v = (icon ?? "").Trim().ToLowerInvariant();
        return v is "award" or "star" or "check" or "flag" ? v : "award";
    }

    public static string NormHue(string? hue)
    {
        var v = (hue ?? "").Trim().ToLowerInvariant();
        return v is "teal" or "yellow" or "blue" or "coral" or "indigo" or "red" or "slate" or "amber" or "pink"
            ? v
            : "yellow";
    }

    private static AchievementResponse Computed(
        Guid studentId, string kind, string title, DateTime day, string icon, string hue) =>
        new($"computed:{kind}:{studentId:D}", title, day, icon, hue);
}
