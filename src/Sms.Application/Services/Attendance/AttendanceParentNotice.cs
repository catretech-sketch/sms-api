namespace Sms.Application.Services.Attendance;

/// Absent marks notify linked parents. Present/late/leave corrections refresh via live attendance.
public static class AttendanceParentNotice
{
    public static bool IsAbsent(string? status) =>
        string.Equals((status ?? "").Trim(), "absent", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Guid> AbsentStudentIds(
        IEnumerable<(Guid StudentId, string? Status)> records) =>
        records
            .Where(r => IsAbsent(r.Status))
            .Select(r => r.StudentId)
            .Distinct()
            .ToList();

    public static string Title(string studentName)
    {
        var name = (studentName ?? "").Trim();
        return string.IsNullOrEmpty(name) ? "Student marked absent" : $"{name} marked absent";
    }

    public static string Body(DateTime date, string? subject = null, int? period = null)
    {
        var when = date.ToString("yyyy-MM-dd");
        var subj = (subject ?? "").Trim();
        if (subj.Length > 0 && period is > 0)
            return $"{subj} · period {period} · {when}";
        if (subj.Length > 0)
            return $"{subj} · {when}";
        return when;
    }
}
