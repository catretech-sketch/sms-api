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

    private async Task<IReadOnlyList<PrincipalStaffEntry>> LoadStaffAsync(DateTime d, CancellationToken ct)
    {
        var staffRows = await QueryInlineAsync<StaffRow>(@"
SELECT t.Id AS TeacherId, t.Name, t.SubjectsCsv, t.Phone, t.Designation,
       (SELECT MAX(ci.At) FROM dbo.CheckIns ci
          JOIN dbo.Users u ON u.Id = ci.UserId
          WHERE u.Email = t.Email AND ci.Kind = 'in' AND ci.Verified = 1
            AND CAST(ci.At AS date) = @d) AS CheckInAt
FROM dbo.Teachers t
WHERE t.Status = 'active'
ORDER BY t.Name", new { d }, ct);

        return staffRows.Select(r => new PrincipalStaffEntry(
            r.TeacherId, r.Name, Initials(r.Name),
            string.IsNullOrEmpty(r.SubjectsCsv) ? null : r.SubjectsCsv.Split(',')[0],
            r.Phone, r.CheckInAt is not null, r.CheckInAt,
            string.IsNullOrEmpty(r.Designation) ? "teacher" : r.Designation)).ToList();
    }

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

        var staff = await LoadStaffAsync(d, ct);
        return new PrincipalOverviewResponse(kpiRows[0], staff);
    }

    public async Task<PrincipalAttendanceResponse> GetPrincipalAttendanceAsync(DateTime today, CancellationToken ct = default)
    {
        var d = today.Date;
        var classes = await QueryInlineAsync<PrincipalClassAttendance>(@"
SELECT c.Id AS ClassId, c.Name AS ClassName,
       ISNULL(a.Present, 0) AS Present, c.StudentCount AS Total,
       CAST(CASE WHEN c.StudentCount > 0 THEN 100.0 * ISNULL(a.Present,0) / c.StudentCount ELSE 0 END AS decimal(5,1)) AS Pct
FROM dbo.Classes c
OUTER APPLY (SELECT COUNT(*) AS Present FROM dbo.AttendanceRecords ar
             WHERE ar.ClassId = c.Id AND ar.[Date] = @d AND ar.Status IN ('present','late')) a
ORDER BY c.Name", new { d }, ct);

        int presentTotal = classes.Sum(c => c.Present);
        int studentTotal = classes.Sum(c => c.Total);
        decimal overall = studentTotal > 0 ? Math.Round(100m * presentTotal / studentTotal, 1) : 0m;
        var staff = await LoadStaffAsync(d, ct);
        return new PrincipalAttendanceResponse(d, presentTotal, studentTotal, overall, classes, staff);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
