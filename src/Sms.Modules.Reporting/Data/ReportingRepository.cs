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

    private sealed record PunchRow(Guid UserId, string Kind, DateTime At, bool Verified);

    private sealed record RosterPersonRow(
        Guid PersonId, string Name, string? Email, string? Phone,
        string? SubjectsCsv, string? Designation, string? Role, Guid? UserId);

    private sealed record UserRow(Guid Id, string? Email, string? Phone);

    private sealed record DayPunches(DateTime? CheckInAt, bool CheckInVerified, DateTime? CheckOutAt);

    private static bool IsKind(string kind, string expected) =>
        string.Equals(kind.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static (DateTime StartUtc, DateTime EndUtc) LocalDayBoundsUtc(DateOnly day, TimeSpan utcOffset)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue);
        var startUtc = localMidnight - utcOffset;
        return (startUtc, startUtc.AddDays(1));
    }

    private static Dictionary<Guid, DayPunches> BuildPunchIndex(IReadOnlyList<PunchRow> rows)
    {
        var dict = new Dictionary<Guid, DayPunches>();
        foreach (var g in rows.GroupBy(r => r.UserId))
        {
            var lastIn = g.Where(x => IsKind(x.Kind, "in")).OrderBy(x => x.At).LastOrDefault();
            var lastOut = g.Where(x => IsKind(x.Kind, "out")).OrderBy(x => x.At).LastOrDefault();
            dict[g.Key] = new DayPunches(lastIn?.At, lastIn?.Verified ?? false, lastOut?.At);
        }
        return dict;
    }

    /// Resolve login user(s) for a roster row. When UserId is set, use only that account.
    /// Email is used as a fallback when the row is not linked. Phone is intentionally excluded —
    /// roster phones are often shared or stale and would attribute one person's punch to others.
    private static IEnumerable<Guid> CandidateUserIds(RosterPersonRow person, IReadOnlyList<UserRow> users)
    {
        var ids = new HashSet<Guid>();
        if (person.UserId is { } direct)
        {
            ids.Add(direct);
            return ids;
        }

        if (string.IsNullOrWhiteSpace(person.Email)) return ids;

        foreach (var u in users)
        {
            if (!string.IsNullOrWhiteSpace(u.Email)
                && string.Equals(person.Email.Trim(), u.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                ids.Add(u.Id);
        }
        return ids;
    }

    /// Merge latest in/out across every login user linked to this teacher/staff row.
    private static DayPunches? MergePunches(IEnumerable<Guid> userIds, Dictionary<Guid, DayPunches> index)
    {
        DateTime? inAt = null, outAt = null;
        var verified = false;
        foreach (var id in userIds)
        {
            if (!index.TryGetValue(id, out var p)) continue;
            if (p.CheckInAt is { } ci && (inAt is null || ci > inAt))
            {
                inAt = ci;
                verified = p.CheckInVerified;
            }
            if (p.CheckOutAt is { } co && (outAt is null || co > outAt))
                outAt = co;
        }
        if (inAt is null && outAt is null) return null;
        return new DayPunches(inAt, verified, outAt);
    }

    private async Task<IReadOnlyList<PrincipalStaffEntry>> LoadStaffAsync(
        DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var punches = await QueryInlineAsync<PunchRow>(@"
SELECT UserId, Kind, At, Verified
FROM dbo.CheckIns
WHERE At >= @startUtc AND At < @endUtc
  AND LOWER(LTRIM(RTRIM(Kind))) IN ('in', 'out')
ORDER BY At",
            new { startUtc, endUtc }, ct);
        var punchIndex = BuildPunchIndex(punches);
        var users = await QueryInlineAsync<UserRow>(
            "SELECT Id, Email, Phone FROM dbo.Users", null, ct);

        var teacherRows = await QueryInlineAsync<RosterPersonRow>(@"
SELECT t.Id AS PersonId, t.Name, t.Email,
       COALESCE(NULLIF(LTRIM(RTRIM(t.Phone)), ''), NULLIF(LTRIM(RTRIM(u.Phone)), '')) AS Phone,
       t.SubjectsCsv, t.Designation,
       CAST(NULL AS nvarchar(64)) AS Role, t.UserId
FROM dbo.Teachers t
LEFT JOIN dbo.Users u ON u.Id = t.UserId
WHERE t.Status = 'active'
ORDER BY t.Name", null, ct);

        var supportRows = await QueryInlineAsync<RosterPersonRow>(@"
SELECT s.Id AS PersonId, s.Name, s.Email,
       COALESCE(NULLIF(LTRIM(RTRIM(s.Phone)), ''), NULLIF(LTRIM(RTRIM(u.Phone)), '')) AS Phone,
       CAST(NULL AS nvarchar(200)) AS SubjectsCsv, CAST(NULL AS nvarchar(64)) AS Designation,
       s.Role, s.UserId
FROM dbo.Staff s
LEFT JOIN dbo.Users u ON u.Id = s.UserId
WHERE s.Status = 'active'
ORDER BY s.Name", null, ct);

        static PrincipalStaffEntry ToEntry(
            RosterPersonRow r, IReadOnlyList<UserRow> users, Dictionary<Guid, DayPunches> index, bool isTeacher)
        {
            var punches = MergePunches(CandidateUserIds(r, users), index);
            var checkInAt = punches?.CheckInAt;
            var checkOutAt = punches?.CheckOutAt;
            return new PrincipalStaffEntry(
                r.PersonId, r.Name, Initials(r.Name),
                isTeacher && !string.IsNullOrEmpty(r.SubjectsCsv) ? r.SubjectsCsv.Split(',')[0] : null,
                r.Phone,
                checkInAt is not null,
                checkInAt,
                checkOutAt,
                checkInAt is not null && (punches?.CheckInVerified ?? false),
                isTeacher ? null : (string.IsNullOrEmpty(r.Role) ? null : r.Role),
                isTeacher ? (string.IsNullOrEmpty(r.Designation) ? "Teacher" : r.Designation) : null);
        }

        var teachers = teacherRows.Select(r => ToEntry(r, users, punchIndex, isTeacher: true)).ToList();
        var support = supportRows.Select(r => ToEntry(r, users, punchIndex, isTeacher: false)).ToList();
        return teachers.Concat(support).OrderBy(s => s.Name).ToList();
    }

    /// School-wide student attendance for dashboard KPIs — distinct students, not per-class sums
    /// (duplicate class rows and grade/section overlap would inflate the denominator).
    private async Task<(int PresentTotal, int StudentTotal)> ResolveStudentTotalsAsync(
        DateTime d, CancellationToken ct)
    {
        var rows = await QueryInlineAsync<StudentTotalsRow>(@"
SELECT
  (SELECT COUNT(*) FROM dbo.Students WHERE Status = N'active') AS StudentTotal,
  (SELECT COUNT(DISTINCT ar.StudentId) FROM dbo.AttendanceRecords ar
     WHERE ar.[Date] = @d AND ar.Status IN ('present','late')) AS PresentTotal",
            new { d }, ct);

        var row = rows.Count > 0 ? rows[0] : new StudentTotalsRow(0, 0);
        return (row.PresentTotal, row.StudentTotal);
    }

    private sealed record StudentTotalsRow(int StudentTotal, int PresentTotal);

    public async Task<PrincipalOverviewResponse> GetPrincipalOverviewAsync(
        DateOnly day, TimeSpan utcOffset, CancellationToken ct = default)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        var (startUtc, endUtc) = LocalDayBoundsUtc(day, utcOffset);
        var staff = await LoadStaffAsync(startUtc, endUtc, ct);
        var staffPresent = staff.Count(s => s.CheckedIn);

        var (presentTotal, studentTotal) = await ResolveStudentTotalsAsync(d, ct);
        var studentsPct = studentTotal > 0
            ? Math.Round(100m * presentTotal / studentTotal, 1)
            : 0m;

        var pendingRows = await QueryInlineAsync<int>(
            "SELECT COUNT(*) FROM dbo.LeaveRequests WHERE Status = 'pending'", null, ct);
        var pendingApprovals = pendingRows.Count > 0 ? pendingRows[0] : 0;

        var kpis = new PrincipalKpis(studentsPct, staffPresent, staff.Count, pendingApprovals);
        return new PrincipalOverviewResponse(kpis, staff);
    }

    public async Task<PrincipalAttendanceResponse> GetPrincipalAttendanceAsync(
        DateOnly day, TimeSpan utcOffset, CancellationToken ct = default)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        var (startUtc, endUtc) = LocalDayBoundsUtc(day, utcOffset);
        var classes = await QueryInlineAsync<PrincipalClassAttendance>(@"
SELECT c.Id AS ClassId, c.Name AS ClassName,
       ISNULL(a.Present, 0) AS Present,
       CASE
         WHEN c.StudentCount > 0 THEN c.StudentCount
         ELSE ISNULL(sc.Cnt, 0)
       END AS Total,
       CAST(CASE
         WHEN (CASE WHEN c.StudentCount > 0 THEN c.StudentCount ELSE ISNULL(sc.Cnt, 0) END) > 0
         THEN 100.0 * ISNULL(a.Present, 0)
              / (CASE WHEN c.StudentCount > 0 THEN c.StudentCount ELSE ISNULL(sc.Cnt, 0) END)
         ELSE 0 END AS decimal(5,1)) AS Pct
FROM dbo.Classes c
OUTER APPLY (
  SELECT COUNT(*) AS Present FROM dbo.AttendanceRecords ar
  WHERE ar.ClassId = c.Id AND ar.[Date] = @d AND ar.Status IN ('present','late')
) a
OUTER APPLY (
  SELECT COUNT(*) AS Cnt FROM dbo.Students s
  WHERE s.Status = N'active'
    AND (
      (c.Grade IS NOT NULL AND c.Section IS NOT NULL
        AND s.Grade = c.Grade AND s.Section = c.Section)
      OR (c.Name IS NOT NULL AND s.ClassLabel = c.Name)
    )
) sc
ORDER BY c.Name", new { d }, ct);

        var (presentTotal, studentTotal) = await ResolveStudentTotalsAsync(d, ct);
        decimal overall = studentTotal > 0 ? Math.Round(100m * presentTotal / studentTotal, 1) : 0m;
        var staff = await LoadStaffAsync(startUtc, endUtc, ct);
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
