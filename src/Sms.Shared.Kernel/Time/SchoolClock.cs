namespace Sms.Shared.Kernel.Time;

/// <summary>
/// Converts a UTC instant to the school's local wall-clock time (India Standard Time), the single
/// shared convention used anywhere a feature needs "what time is it at the school right now" rather
/// than raw UTC (e.g. "today" for attendance/calendar purposes, or a time-of-day greeting bucket).
/// Falls back to a fixed +5:30 offset when the "India Standard Time" / "Asia/Kolkata" timezone id
/// isn't registered in the current environment.
/// </summary>
public static class SchoolClock
{
    public static DateTime ToSchoolLocal(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
            : utcNow.ToUniversalTime();
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.AddHours(5).AddMinutes(30);
        }
    }
}
