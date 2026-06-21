using Sms.Modules.Reporting.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Reporting.Data;

public sealed class ReportingRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<DashboardStatsResponse> GetDashboardStatsAsync(DateTime today, CancellationToken ct = default)
    {
        var row = await QueryInlineAsync<DashboardStatsResponse>(@"
SELECT
  (SELECT COUNT(*) FROM dbo.Students)                                            AS TotalStudents,
  (SELECT COUNT(*) FROM dbo.Classes)                                             AS TotalClasses,
  (SELECT COUNT(*) FROM dbo.AttendanceRecords
     WHERE [Date] = @today AND Status IN ('present','late'))                     AS AttendanceToday,
  (SELECT COUNT(*) FROM dbo.Homework
     WHERE Status = 'todo' AND (DueDate IS NULL OR DueDate >= @today))           AS PendingAssignments,
  (SELECT COUNT(*) FROM dbo.ExamPapers
     WHERE Status = 'upcoming' AND ([Date] IS NULL OR [Date] >= @today))         AS UpcomingExams",
            new { today = today.Date }, ct);
        return row[0];
    }
}
