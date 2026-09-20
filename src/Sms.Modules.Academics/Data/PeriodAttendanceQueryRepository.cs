using Dapper;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class PeriodAttendanceQueryRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<PeriodAttendanceAdvancedPage> SearchAsync(
        PeriodAttendanceAdvancedQuery q,
        CancellationToken ct = default)
    {
        var command = PeriodAttendanceQuerySql.Build(q);
        await using var conn = await Factory.OpenAsync(ct);
        using var results = await conn.QueryMultipleAsync(
            new CommandDefinition(command.Sql, command.Parameters, cancellationToken: ct));
        var totalCount = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<PeriodAttendanceAdvancedRow>()).AsList();

        return command.ToPage(items, totalCount);
    }

    public async Task<AdvClassDaySummary> SummarizeClassDayAsync(
        Guid classId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var command = PeriodAttendanceAggregateSql.BuildClassDay(classId, date);
        await using var conn = await Factory.OpenAsync(ct);
        var row = await conn.QuerySingleAsync<PeriodAttendanceClassDayRow>(
            new CommandDefinition(command.Sql, command.Parameters, cancellationToken: ct));
        return row.ToContract();
    }

    public async Task<IReadOnlyList<AdvSubjectSummaryRow>> SummarizeSubjectsAsync(
        Guid classId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var command = PeriodAttendanceAggregateSql.BuildSubjects(classId, from, to);
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<PeriodAttendanceSubjectRow>(
            new CommandDefinition(command.Sql, command.Parameters, cancellationToken: ct));
        return rows.Select(row => row.ToContract()).ToList();
    }

    public async Task<IReadOnlyList<AdvTeacherSummaryRow>> SummarizeTeachersAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var command = PeriodAttendanceAggregateSql.BuildTeachers(from, to);
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<PeriodAttendanceTeacherRow>(
            new CommandDefinition(command.Sql, command.Parameters, cancellationToken: ct));
        return rows.Select(row => row.ToContract()).ToList();
    }

    public async Task<AdvRangeRollup> SummarizeRangeAsync(
        DateOnly from,
        DateOnly to,
        Guid? classId = null,
        string? grade = null,
        string? section = null,
        Guid? studentId = null,
        string? subject = null,
        Guid? teacherId = null,
        CancellationToken ct = default)
    {
        var command = PeriodAttendanceAggregateSql.BuildRange(
            from, to, classId, grade, section, studentId, subject, teacherId);
        await using var conn = await Factory.OpenAsync(ct);
        var row = await conn.QuerySingleAsync<PeriodAttendanceRangeRow>(
            new CommandDefinition(command.Sql, command.Parameters, cancellationToken: ct));
        return row.ToContract();
    }

    public async Task<IReadOnlyList<PeriodAttendanceAuditRow>> GetAuditAsync(
        Guid recordId,
        CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<PeriodAttendanceAuditRow>(
            new CommandDefinition(
                """
                SELECT Id, RecordId, ClassId, StudentId, [Date], Period, Subject,
                       FromStatus, ToStatus, ActorId, ActorName, ActorRole, At
                FROM dbo.PeriodAttendanceAudit
                WHERE RecordId = @RecordId
                ORDER BY At DESC
                """,
                new { RecordId = recordId },
                cancellationToken: ct));
        return rows.AsList();
    }
}

public sealed record PeriodAttendanceClassDayRow(
    int TotalStudents,
    int Present,
    int Absent,
    int Late,
    int Leave,
    int TotalPeriods,
    int MarkedPeriods)
{
    public AdvClassDaySummary ToContract()
    {
        var counts = PeriodAttendanceMath.FromStatusBuckets(Present, Late, Absent, Leave);
        return new AdvClassDaySummary(
            TotalStudents,
            Present,
            Absent,
            Late,
            Leave,
            Math.Max(0, (TotalStudents * TotalPeriods) - counts.TotalMarkedPeriods),
            counts.AttendancePercentage,
            TotalPeriods,
            MarkedPeriods,
            Math.Max(0, TotalPeriods - MarkedPeriods));
    }
}

public sealed record PeriodAttendanceSubjectRow(
    string Subject,
    string? TeacherName,
    int Periods,
    int Marked,
    int Present,
    int Absent,
    int Late,
    int Leave)
{
    public AdvSubjectSummaryRow ToContract()
    {
        var counts = PeriodAttendanceMath.FromStatusBuckets(Present, Late, Absent, Leave);
        return new AdvSubjectSummaryRow(
            Subject,
            TeacherName,
            Periods,
            Marked,
            Math.Max(0, Periods - Marked),
            Present,
            Absent,
            Late,
            counts.AttendancePercentage);
    }
}

public sealed record PeriodAttendanceTeacherRow(
    Guid TeacherId,
    string TeacherName,
    int Classes,
    int Sections,
    int Subjects,
    int ExpectedPeriods,
    int MarkedPeriods,
    int TeacherMarked,
    int StaffMarked,
    int PrincipalMarked,
    int AdminMarked)
{
    public AdvTeacherSummaryRow ToContract() =>
        new(
            TeacherId,
            TeacherName,
            Classes,
            Sections,
            Subjects,
            ExpectedPeriods,
            MarkedPeriods,
            Math.Max(0, ExpectedPeriods - MarkedPeriods),
            TeacherMarked,
            StaffMarked,
            PrincipalMarked,
            AdminMarked);
}

public sealed record PeriodAttendanceRangeRow(int Present, int Absent, int Late, int Leave)
{
    public AdvRangeRollup ToContract()
    {
        var counts = PeriodAttendanceMath.FromStatusBuckets(Present, Late, Absent, Leave);
        return new AdvRangeRollup(
            counts.TotalMarkedPeriods,
            Present,
            Absent,
            Late,
            Leave,
            counts.AttendancePercentage);
    }
}

public sealed record PeriodAttendanceAggregateCommand(string Sql, DynamicParameters Parameters);

public static class PeriodAttendanceAggregateSql
{
    private const string DateSeries = """
        WITH Dates AS (
            SELECT CAST(@From AS date) AS [Date]
            UNION ALL
            SELECT DATEADD(DAY, 1, [Date])
            FROM Dates
            WHERE [Date] < CAST(@To AS date)
        )
        """;

    public static PeriodAttendanceAggregateCommand BuildClassDay(Guid classId, DateOnly date)
    {
        const string sql = """
            WITH ExpectedSessions AS (
                SELECT ts.Period, LOWER(LTRIM(RTRIM(ts.Subject))) AS Subject
                FROM dbo.TimetableSlots ts
                WHERE ts.ClassId = @ClassId
                  AND UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3))
                      = UPPER(LEFT(DATENAME(WEEKDAY, @Date), 3))
            ),
            StatusCounts AS (
                SELECT
                    ISNULL(SUM(CASE WHEN par.Status = N'present' THEN 1 ELSE 0 END), 0) AS Present,
                    ISNULL(SUM(CASE WHEN par.Status = N'absent' THEN 1 ELSE 0 END), 0) AS Absent,
                    ISNULL(SUM(CASE WHEN par.Status = N'late' THEN 1 ELSE 0 END), 0) AS Late,
                    ISNULL(SUM(CASE WHEN par.Status = N'leave' THEN 1 ELSE 0 END), 0) AS Leave
                FROM ExpectedSessions es
                LEFT JOIN dbo.PeriodAttendanceRecords par
                  ON par.ClassId = @ClassId
                 AND par.[Date] = @Date
                 AND par.Period = es.Period
                 AND LOWER(LTRIM(RTRIM(par.Subject))) = es.Subject
            ),
            MarkedSessions AS (
                SELECT par.Period, LOWER(LTRIM(RTRIM(par.Subject))) AS Subject
                FROM dbo.PeriodAttendanceRecords par
                WHERE par.ClassId = @ClassId AND par.[Date] = @Date
                GROUP BY par.Period, LOWER(LTRIM(RTRIM(par.Subject)))
            )
            SELECT
                (SELECT COUNT(*)
                 FROM dbo.Classes c
                 INNER JOIN dbo.Students s
                   ON (c.Grade IS NOT NULL AND c.Section IS NOT NULL
                       AND s.Grade = c.Grade AND s.Section = c.Section)
                   OR (c.Name IS NOT NULL AND s.ClassLabel = c.Name)
                 WHERE c.Id = @ClassId AND s.Status = N'active') AS TotalStudents,
                sc.Present,
                sc.Absent,
                sc.Late,
                sc.Leave,
                (SELECT COUNT(*) FROM ExpectedSessions) AS TotalPeriods,
                (SELECT COUNT(*) FROM MarkedSessions ms
                 WHERE EXISTS (
                     SELECT 1 FROM ExpectedSessions es
                     WHERE es.Period = ms.Period AND es.Subject = ms.Subject
                 )) AS MarkedPeriods
            FROM StatusCounts sc;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("ClassId", classId);
        parameters.Add("Date", date.ToDateTime(TimeOnly.MinValue));
        return new PeriodAttendanceAggregateCommand(sql, parameters);
    }

    public static PeriodAttendanceAggregateCommand BuildSubjects(
        Guid classId,
        DateOnly from,
        DateOnly to)
    {
        ValidateRange(from, to);
        var sql = DateSeries + """
            , ExpectedSessions AS (
                SELECT
                    d.[Date],
                    ts.ClassId,
                    ts.Period,
                    LTRIM(RTRIM(ts.Subject)) AS Subject,
                    LOWER(LTRIM(RTRIM(ts.Subject))) AS SubjectKey,
                    t.Name AS TeacherName
                FROM Dates d
                INNER JOIN dbo.TimetableSlots ts
                  ON ts.ClassId = @ClassId
                 AND UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3))
                     = UPPER(LEFT(DATENAME(WEEKDAY, d.[Date]), 3))
                LEFT JOIN dbo.Teachers t ON t.Id = ts.TeacherId
                WHERE NULLIF(LTRIM(RTRIM(ts.Subject)), N'') IS NOT NULL
            ),
            MarkedSessions AS (
                SELECT
                    par.[Date],
                    par.ClassId,
                    par.Period,
                    LOWER(LTRIM(RTRIM(par.Subject))) AS SubjectKey,
                    SUM(CASE WHEN par.Status = N'present' THEN 1 ELSE 0 END) AS Present,
                    SUM(CASE WHEN par.Status = N'absent' THEN 1 ELSE 0 END) AS Absent,
                    SUM(CASE WHEN par.Status = N'late' THEN 1 ELSE 0 END) AS Late,
                    SUM(CASE WHEN par.Status = N'leave' THEN 1 ELSE 0 END) AS Leave
                FROM dbo.PeriodAttendanceRecords par
                WHERE par.ClassId = @ClassId
                  AND par.[Date] >= @From AND par.[Date] <= @To
                GROUP BY par.[Date], par.ClassId, par.Period,
                         LOWER(LTRIM(RTRIM(par.Subject)))
            )
            SELECT
                es.Subject,
                es.TeacherName,
                COUNT(*) AS Periods,
                SUM(CASE WHEN ms.Period IS NOT NULL THEN 1 ELSE 0 END) AS Marked,
                ISNULL(SUM(ms.Present), 0) AS Present,
                ISNULL(SUM(ms.Absent), 0) AS Absent,
                ISNULL(SUM(ms.Late), 0) AS Late,
                ISNULL(SUM(ms.Leave), 0) AS Leave
            FROM ExpectedSessions es
            LEFT JOIN MarkedSessions ms
              ON ms.[Date] = es.[Date]
             AND ms.ClassId = es.ClassId
             AND ms.Period = es.Period
             AND ms.SubjectKey = es.SubjectKey
            GROUP BY es.Subject, es.TeacherName
            ORDER BY es.Subject, es.TeacherName
            OPTION (MAXRECURSION 32767);
            """;

        return BuildDateRangeCommand(sql, from, to, ("ClassId", classId));
    }

    public static PeriodAttendanceAggregateCommand BuildTeachers(DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        var sql = DateSeries + """
            , ExpectedSessions AS (
                SELECT
                    d.[Date],
                    ts.ClassId,
                    c.Grade,
                    c.Section,
                    ts.Period,
                    LTRIM(RTRIM(ts.Subject)) AS Subject,
                    LOWER(LTRIM(RTRIM(ts.Subject))) AS SubjectKey,
                    ts.TeacherId,
                    t.Name AS TeacherName
                FROM Dates d
                INNER JOIN dbo.TimetableSlots ts
                  ON UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3))
                     = UPPER(LEFT(DATENAME(WEEKDAY, d.[Date]), 3))
                INNER JOIN dbo.Classes c ON c.Id = ts.ClassId
                INNER JOIN dbo.Teachers t ON t.Id = ts.TeacherId
                WHERE ts.TeacherId IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM(ts.Subject)), N'') IS NOT NULL
            ),
            MarkedSessions AS (
                SELECT
                    par.[Date],
                    par.ClassId,
                    par.Period,
                    LOWER(LTRIM(RTRIM(par.Subject))) AS SubjectKey,
                    MAX(REPLACE(LOWER(par.MarkedByRole), N'school.', N'')) AS MarkerRole
                FROM dbo.PeriodAttendanceRecords par
                WHERE par.[Date] >= @From AND par.[Date] <= @To
                GROUP BY par.[Date], par.ClassId, par.Period,
                         LOWER(LTRIM(RTRIM(par.Subject)))
            )
            SELECT
                es.TeacherId,
                es.TeacherName,
                COUNT(DISTINCT NULLIF(LTRIM(RTRIM(es.Grade)), N'')) AS Classes,
                COUNT(DISTINCT es.ClassId) AS Sections,
                COUNT(DISTINCT es.SubjectKey) AS Subjects,
                COUNT(*) AS ExpectedPeriods,
                SUM(CASE WHEN ms.Period IS NOT NULL THEN 1 ELSE 0 END) AS MarkedPeriods,
                SUM(CASE WHEN ms.MarkerRole = N'teacher' THEN 1 ELSE 0 END) AS TeacherMarked,
                SUM(CASE WHEN ms.MarkerRole = N'staff' THEN 1 ELSE 0 END) AS StaffMarked,
                SUM(CASE WHEN ms.MarkerRole = N'principal' THEN 1 ELSE 0 END) AS PrincipalMarked,
                SUM(CASE WHEN ms.MarkerRole = N'admin' THEN 1 ELSE 0 END) AS AdminMarked
            FROM ExpectedSessions es
            LEFT JOIN MarkedSessions ms
              ON ms.[Date] = es.[Date]
             AND ms.ClassId = es.ClassId
             AND ms.Period = es.Period
             AND ms.SubjectKey = es.SubjectKey
            GROUP BY es.TeacherId, es.TeacherName
            ORDER BY es.TeacherName
            OPTION (MAXRECURSION 32767);
            """;

        return BuildDateRangeCommand(sql, from, to);
    }

    public static PeriodAttendanceAggregateCommand BuildRange(
        DateOnly from,
        DateOnly to,
        Guid? classId,
        string? grade,
        string? section,
        Guid? studentId,
        string? subject,
        Guid? teacherId)
    {
        ValidateRange(from, to);
        const string sql = """
            SELECT
                ISNULL(SUM(CASE WHEN par.Status = N'present' THEN 1 ELSE 0 END), 0) AS Present,
                ISNULL(SUM(CASE WHEN par.Status = N'absent' THEN 1 ELSE 0 END), 0) AS Absent,
                ISNULL(SUM(CASE WHEN par.Status = N'late' THEN 1 ELSE 0 END), 0) AS Late,
                ISNULL(SUM(CASE WHEN par.Status = N'leave' THEN 1 ELSE 0 END), 0) AS Leave
            FROM dbo.PeriodAttendanceRecords par
            INNER JOIN dbo.Classes c ON c.Id = par.ClassId
            LEFT JOIN dbo.TimetableSlots ts
              ON ts.ClassId = par.ClassId
             AND ts.Period = par.Period
             AND UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3))
                 = UPPER(LEFT(DATENAME(WEEKDAY, par.[Date]), 3))
             AND LOWER(LTRIM(RTRIM(ts.Subject))) = LOWER(LTRIM(RTRIM(par.Subject)))
            WHERE par.[Date] >= @From AND par.[Date] <= @To
              AND (@ClassId IS NULL OR par.ClassId = @ClassId)
              AND (@Grade IS NULL OR c.Grade = @Grade)
              AND (@Section IS NULL OR c.Section = @Section)
              AND (@StudentId IS NULL OR par.StudentId = @StudentId)
              AND (@Subject IS NULL
                   OR LOWER(LTRIM(RTRIM(par.Subject))) = LOWER(LTRIM(RTRIM(@Subject))))
              AND (@TeacherId IS NULL OR ts.TeacherId = @TeacherId);
            """;

        var parameters = DateRangeParameters(from, to);
        parameters.Add("ClassId", classId);
        parameters.Add("Grade", Clean(grade));
        parameters.Add("Section", Clean(section));
        parameters.Add("StudentId", studentId);
        parameters.Add("Subject", Clean(subject));
        parameters.Add("TeacherId", teacherId);
        return new PeriodAttendanceAggregateCommand(sql, parameters);
    }

    private static PeriodAttendanceAggregateCommand BuildDateRangeCommand(
        string sql,
        DateOnly from,
        DateOnly to,
        params (string Name, object? Value)[] extra)
    {
        var parameters = DateRangeParameters(from, to);
        foreach (var (name, value) in extra) parameters.Add(name, value);
        return new PeriodAttendanceAggregateCommand(sql, parameters);
    }

    private static DynamicParameters DateRangeParameters(DateOnly from, DateOnly to)
    {
        var parameters = new DynamicParameters();
        parameters.Add("From", from.ToDateTime(TimeOnly.MinValue));
        parameters.Add("To", to.ToDateTime(TimeOnly.MinValue));
        return parameters;
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "To must be on or after From.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PeriodAttendanceQueryCommand(
    string Sql,
    DynamicParameters Parameters,
    int Page,
    int PageSize)
{
    public PeriodAttendanceAdvancedPage ToPage(
        IReadOnlyList<PeriodAttendanceAdvancedRow> items,
        int totalCount) =>
        new(items, totalCount, Page, PageSize);
}

public static class PeriodAttendanceQuerySql
{
    private const string FromAndJoins = """
        FROM dbo.PeriodAttendanceRecords par
        INNER JOIN dbo.Students s ON s.Id = par.StudentId
        INNER JOIN dbo.Classes c ON c.Id = par.ClassId
        LEFT JOIN dbo.Users u ON u.Id = par.MarkedBy
        LEFT JOIN dbo.Users uu ON uu.Id = par.UpdatedBy
        LEFT JOIN dbo.TimetableSlots ts
          ON ts.ClassId = par.ClassId
         AND ts.Period = par.Period
         AND UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3)) = UPPER(LEFT(DATENAME(WEEKDAY, par.[Date]), 3))
         AND LOWER(LTRIM(RTRIM(ts.Subject))) = LOWER(LTRIM(RTRIM(par.Subject)))
        LEFT JOIN dbo.Teachers t ON t.Id = ts.TeacherId
        """;

    private const string Filters = """
        WHERE par.[Date] >= @From
          AND par.[Date] <= @To
          AND (@ClassId IS NULL OR par.ClassId = @ClassId)
          AND (@Grade IS NULL OR c.Grade = @Grade)
          AND (@Section IS NULL OR c.Section = @Section)
          AND (@Subject IS NULL OR LOWER(LTRIM(RTRIM(par.Subject))) = LOWER(LTRIM(RTRIM(@Subject))))
          AND (@Period IS NULL OR par.Period = @Period)
          AND (@AssignedTeacherId IS NULL OR ts.TeacherId = @AssignedTeacherId)
          AND (@AuthorizedTeacherId IS NULL
               OR ts.TeacherId = @AuthorizedTeacherId
               OR c.ClassTeacherId = @AuthorizedTeacherId)
          AND (@MarkedBy IS NULL OR par.MarkedBy = @MarkedBy)
          AND (@MarkedByRole IS NULL
               OR REPLACE(LOWER(par.MarkedByRole), N'school.', N'') = LOWER(@MarkedByRole))
          AND (@Status IS NULL OR par.Status = @Status)
          AND (@GeoFenceStatus IS NULL OR COALESCE(par.GeoFenceStatus, N'not_required') = @GeoFenceStatus)
          AND (@Q IS NULL OR s.Name LIKE N'%' + @Q + N'%' OR s.AdmissionNo LIKE N'%' + @Q + N'%')
        """;

    private const string Sql = "SELECT COUNT(*)\n" + FromAndJoins + "\n" + Filters + ";\n" + """
        SELECT par.Id,
               par.ClassId,
               COALESCE(c.Grade, N'') AS Grade,
               COALESCE(c.Section, N'') AS Section,
               c.Name AS ClassLabel,
               par.StudentId,
               s.Name AS StudentName,
               COALESCE(s.AdmissionNo, N'') AS AdmissionNo,
               par.[Date],
               par.Period,
               par.PeriodId,
               par.Subject,
               par.SubjectId,
               ts.StartTime,
               ts.EndTime,
               par.Status,
               ts.TeacherId AS AssignedTeacherId,
               t.Name AS AssignedTeacherName,
               par.MarkedBy,
               u.Name AS MarkedByName,
               REPLACE(LOWER(par.MarkedByRole), N'school.', N'') AS MarkedByRole,
               COALESCE(par.UpdatedAt, par.CreatedAt) AS MarkedAt,
               COALESCE(par.GeoFenceStatus, N'not_required') AS GeoFenceStatus,
               par.GeoDistanceMeters,
               par.GeoCapturedAt,
               par.UpdatedBy,
               uu.Name AS UpdatedByName,
               REPLACE(LOWER(par.UpdatedByRole), N'school.', N'') AS UpdatedByRole,
               par.UpdatedAt
        """ + "\n" + FromAndJoins + "\n" + Filters + "\n" + """
        ORDER BY par.[Date] DESC, c.Name, par.Period, s.Name
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public static PeriodAttendanceQueryCommand Build(PeriodAttendanceAdvancedQuery q)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = q.PageSize <= 0 ? 25 : Math.Clamp(q.PageSize, 1, 100);
        var parameters = new DynamicParameters();
        parameters.Add("From", q.From.ToDateTime(TimeOnly.MinValue));
        parameters.Add("To", q.To.ToDateTime(TimeOnly.MinValue));
        parameters.Add("ClassId", q.ClassId);
        parameters.Add("Grade", Clean(q.Grade));
        parameters.Add("Section", Clean(q.Section));
        parameters.Add("Subject", Clean(q.Subject));
        parameters.Add("Period", q.Period);
        parameters.Add("AssignedTeacherId", q.AssignedTeacherId);
        parameters.Add("AuthorizedTeacherId", q.AuthorizedTeacherId);
        parameters.Add("MarkedBy", q.MarkedBy);
        parameters.Add("MarkedByRole", Clean(q.MarkedByRole));
        parameters.Add("Status", Clean(q.Status));
        parameters.Add("GeoFenceStatus", Clean(q.GeoFenceStatus));
        parameters.Add("Q", Clean(q.Q));
        parameters.Add("Offset", (page - 1L) * pageSize);
        parameters.Add("PageSize", pageSize);

        return new PeriodAttendanceQueryCommand(Sql, parameters, page, pageSize);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
