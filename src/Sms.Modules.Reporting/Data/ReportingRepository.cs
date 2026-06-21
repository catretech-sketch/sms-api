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

    private sealed record StaffRow(
        Guid TeacherId, string Name, string? SubjectsCsv, string? Phone, string? Designation,
        DateTime? CheckInAt);

    public async Task<PrincipalOverviewResponse> GetPrincipalOverviewAsync(DateTime today, CancellationToken ct = default)
    {
        var d = today.Date;

        var kpiRows = await QueryInlineAsync<PrincipalKpis>(@"
SELECT
  CAST(CASE WHEN (SELECT SUM(StudentCount) FROM dbo.Classes) > 0
       THEN 100.0 * (SELECT COUNT(*) FROM dbo.AttendanceRecords
                     WHERE [Date] = @d AND Status IN ('present','late'))
              / (SELECT SUM(StudentCount) FROM dbo.Classes)
       ELSE 0 END AS decimal(5,1))                                              AS StudentsPresentPct,
  (SELECT COUNT(DISTINCT t.Id) FROM dbo.Teachers t
     JOIN dbo.Users u ON u.Email = t.Email
     JOIN dbo.CheckIns ci ON ci.UserId = u.Id
     WHERE ci.Kind = 'in' AND ci.Verified = 1 AND CAST(ci.At AS date) = @d)     AS StaffPresent,
  (SELECT COUNT(*) FROM dbo.Teachers WHERE Status = 'active')                   AS StaffTotal,
  (SELECT COUNT(*) FROM dbo.LeaveRequests WHERE Status = 'pending')             AS PendingApprovals",
            new { d }, ct);

        var staffRows = await QueryInlineAsync<StaffRow>(@"
SELECT t.Id AS TeacherId, t.Name, t.SubjectsCsv, t.Phone, t.Designation,
       (SELECT MAX(ci.At) FROM dbo.CheckIns ci
          JOIN dbo.Users u ON u.Id = ci.UserId
          WHERE u.Email = t.Email AND ci.Kind = 'in' AND ci.Verified = 1
            AND CAST(ci.At AS date) = @d) AS CheckInAt
FROM dbo.Teachers t
WHERE t.Status = 'active'
ORDER BY t.Name", new { d }, ct);

        var staff = staffRows.Select(r => new PrincipalStaffEntry(
            r.TeacherId, r.Name, Initials(r.Name),
            string.IsNullOrEmpty(r.SubjectsCsv) ? null : r.SubjectsCsv.Split(',')[0],
            r.Phone, r.CheckInAt is not null, r.CheckInAt,
            string.IsNullOrEmpty(r.Designation) ? "teacher" : r.Designation)).ToList();

        return new PrincipalOverviewResponse(kpiRows[0], staff);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
