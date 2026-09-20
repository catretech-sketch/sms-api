namespace Sms.Application.Services.AiSearch;

public static class AiIntentAccessRules
{
    private const string Admin = "school.admin";
    private const string Owner = "school.owner";
    private const string Principal = "school.principal";
    private const string Teacher = "school.teacher";
    private const string Staff = "staff";
    private const string Parent = "student.parent";
    private const string Driver = "driver";

    private static readonly Dictionary<string, string[]> Rules = new()
    {
        ["DailyAttendanceSummary"] = [Admin, Owner, Principal, Teacher],
        ["ClassAttendance"] = [Admin, Owner, Principal, Teacher],
        ["SectionAttendance"] = [Admin, Owner, Principal, Teacher],
        ["StudentAttendance"] = [Admin, Owner, Principal, Teacher, Parent],
        ["TeacherAttendance"] = [Admin, Owner, Principal, Teacher],
        ["StaffAttendance"] = [Admin, Owner, Principal, Staff],
        ["DashboardSummary"] = [Admin, Owner, Principal],
        ["StudentSearch"] = [Admin, Owner, Principal, Teacher, Parent],
        ["StudentDetails"] = [Admin, Owner, Principal, Teacher, Parent],
        ["TeacherSearch"] = [Admin, Owner, Principal],
        ["StaffSearch"] = [Admin, Owner, Principal],
        ["UpcomingExamSearch"] = [Admin, Owner, Principal, Teacher, Parent],
        ["TestSearch"] = [Admin, Owner, Principal, Teacher, Parent],
        ["HomeworkSearch"] = [Admin, Owner, Principal, Teacher, Parent],
        ["SubjectSearch"] = [Admin, Owner, Principal, Teacher, Parent],
        ["BusLocationSearch"] = [Admin, Owner, Principal, Staff, Parent],
        ["GreetById"] = [Admin, Owner, Principal, Teacher, Staff, Parent],
        ["PersonLookup"] = [Admin, Owner, Principal, Teacher, Staff, Parent],
        ["MyTripStatus"] = [Driver],
    };

    public static IReadOnlySet<string> KnownIntents { get; } = new HashSet<string>(Rules.Keys);

    public static bool IsAllowed(string intent, IEnumerable<string> callerRoles)
    {
        if (!Rules.TryGetValue(intent, out var allowed)) return false;
        var roles = new HashSet<string>(callerRoles, StringComparer.OrdinalIgnoreCase);
        return allowed.Any(roles.Contains);
    }
}
