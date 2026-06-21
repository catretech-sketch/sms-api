namespace Sms.Modules.Reporting.Contracts;

public sealed record DashboardStatsResponse(
    int TotalStudents, int TotalClasses, int AttendanceToday, int PendingAssignments, int UpcomingExams);
