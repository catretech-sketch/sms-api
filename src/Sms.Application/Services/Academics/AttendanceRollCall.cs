using System.Globalization;

namespace Sms.Application.Services.Academics;

public static class AttendanceRollCall
{
    public sealed record SlotInput(
        string Day, int Period, string? Subject, Guid? ClassId, Guid? TeacherId,
        string? StartTime = null, string? EndTime = null, string? TeacherName = null);

    public static string DayKey(DateTime date) =>
        // Calendar day only — ignore time/Kind so UTC midnight never shifts the weekday.
        DateOnly.FromDateTime(date).ToString("ddd", CultureInfo.InvariantCulture);

    /// <summary>Normalize stored Day values (Mon / Monday / mon) to invariant 3-letter keys.</summary>
    public static string NormalizeDayKey(string? day)
    {
        if (string.IsNullOrWhiteSpace(day)) return "";
        var t = day.Trim();
        if (t.Length > 3) t = t[..3];
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToLowerInvariant());
    }

    public static bool SameDay(string? storedDay, string dayKey) =>
        string.Equals(NormalizeDayKey(storedDay), NormalizeDayKey(dayKey), StringComparison.Ordinal);

    public static bool IsNonTeaching(string? subject)
    {
        var s = (subject ?? "").Trim().ToLowerInvariant();
        return s.Contains("lunch") || s.Contains("break") || s.Contains("recess") || s.Contains("assembly");
    }

    public static SlotInput? FirstTeachingSlot(IEnumerable<SlotInput> slots)
    {
        return slots
            .Where(s => !IsNonTeaching(s.Subject))
            .OrderBy(s => s.Period)
            .FirstOrDefault();
    }

    public static SlotInput? Resolve(IEnumerable<SlotInput> slots, DateTime date)
    {
        var day = DayKey(date);
        return FirstTeachingSlot(slots.Where(s => SameDay(s.Day, day)));
    }

    public static bool CanMark(
        bool isLeadership, Guid? callerTeacherId, Guid? classTeacherId, Guid? rollCallTeacherId)
    {
        if (isLeadership) return true;
        if (callerTeacherId is null) return false;
        if (classTeacherId is { } ct && ct == callerTeacherId) return true;
        if (rollCallTeacherId is { } rt && rt == callerTeacherId) return true;
        return false;
    }

    public static string Reason(bool isLeadership, Guid? callerTeacherId, Guid? classTeacherId, Guid? rollCallTeacherId)
    {
        if (isLeadership) return "leadership";
        if (callerTeacherId is { } id && classTeacherId == id) return "class_teacher";
        if (callerTeacherId is { } id2 && rollCallTeacherId == id2) return "first_period";
        return "not_assigned";
    }
}
