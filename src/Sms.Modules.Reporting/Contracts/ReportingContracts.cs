namespace Sms.Modules.Reporting.Contracts;

public sealed record DashboardStatsResponse(
    int TotalStudents, int TotalClasses, int AttendanceToday, int PendingAssignments, int UpcomingExams);

public sealed record PrincipalKpis(
    decimal StudentsPresentPct, int StaffPresent, int StaffTotal, int PendingApprovals);

public sealed record PrincipalStaffEntry(
    Guid TeacherId, string Name, string Initials, string? Subject, string? Phone,
    bool CheckedIn, DateTime? CheckInAt, DateTime? CheckOutAt, bool CheckInVerified, string? Role, string? Designation);

public sealed record PrincipalOverviewResponse(PrincipalKpis Kpis, IReadOnlyList<PrincipalStaffEntry> Staff);

public sealed record PrincipalClassAttendance(
    Guid ClassId, string ClassName, int Present, int Total, decimal Pct, int Marked);

public sealed record PrincipalAttendanceResponse(
    DateTime Date, int PresentTotal, int StudentTotal, decimal OverallPct,
    IReadOnlyList<PrincipalClassAttendance> Classes, IReadOnlyList<PrincipalStaffEntry> Staff);
