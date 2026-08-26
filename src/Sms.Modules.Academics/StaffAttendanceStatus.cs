namespace Sms.Modules.Academics;

/// <summary>
/// Admin/CRM roll-call statuses for teachers and staff.
/// App check-in/out stays on <c>CheckIns</c>; this is the manual mark path
/// for people without a phone.
/// </summary>
public static class StaffAttendanceStatus
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Late = "late";
    public const string HalfDay = "half_day";

    public static string? Normalize(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_');
        return v is Present or Absent or Late or HalfDay ? v : null;
    }

    public static bool IsOnCampus(string? status)
    {
        var v = Normalize(status);
        return v is Present or Late or HalfDay;
    }
}
