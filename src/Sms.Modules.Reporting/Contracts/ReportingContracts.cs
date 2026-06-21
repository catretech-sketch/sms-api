namespace Sms.Modules.Reporting.Contracts;

public sealed record DashboardStatsResponse(
    int TotalStudents, int TotalClasses, int AttendanceToday, int PendingAssignments, int UpcomingExams);

public sealed record PrincipalKpis(
    decimal StudentsPresentPct, int StaffPresent, int StaffTotal, int PendingApprovals);

public sealed record PrincipalStaffEntry(
    Guid TeacherId, string Name, string Initials, string? Subject, string? Phone,
    bool CheckedIn, DateTime? CheckInAt, string? Role);

public sealed record PrincipalOverviewResponse(PrincipalKpis Kpis, IReadOnlyList<PrincipalStaffEntry> Staff);
