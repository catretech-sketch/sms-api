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
        var rows = await QueryInlineAsync<QueryRow>(command.Sql, command.Parameters, ct);
        var items = rows.Select(static row => row.ToContract()).ToList();

        return new PeriodAttendanceAdvancedPage(
            items,
            rows.FirstOrDefault()?.TotalCount ?? 0,
            command.Page,
            command.PageSize);
    }

    private sealed record QueryRow(
        Guid Id,
        Guid ClassId,
        string Grade,
        string Section,
        string ClassLabel,
        Guid StudentId,
        string StudentName,
        string AdmissionNo,
        DateTime Date,
        int Period,
        Guid? PeriodId,
        string Subject,
        Guid? SubjectId,
        string? StartTime,
        string? EndTime,
        string Status,
        Guid? AssignedTeacherId,
        string? AssignedTeacherName,
        Guid? MarkedBy,
        string? MarkedByName,
        string? MarkedByRole,
        DateTime? MarkedAt,
        string GeoFenceStatus,
        int TotalCount)
    {
        public PeriodAttendanceAdvancedRow ToContract() =>
            new(
                Id,
                ClassId,
                Grade,
                Section,
                ClassLabel,
                StudentId,
                StudentName,
                AdmissionNo,
                Date,
                Period,
                PeriodId,
                Subject,
                SubjectId,
                StartTime,
                EndTime,
                Status,
                AssignedTeacherId,
                AssignedTeacherName,
                MarkedBy,
                MarkedByName,
                MarkedByRole,
                MarkedAt,
                GeoFenceStatus);
    }
}

public sealed record PeriodAttendanceQueryCommand(
    string Sql,
    DynamicParameters Parameters,
    int Page,
    int PageSize);

public static class PeriodAttendanceQuerySql
{
    private const string Sql = """
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
               par.MarkedByRole,
               COALESCE(par.UpdatedAt, par.CreatedAt) AS MarkedAt,
               CAST(N'not_required' AS nvarchar(32)) AS GeoFenceStatus,
               COUNT(*) OVER() AS TotalCount
        FROM dbo.PeriodAttendanceRecords par
        INNER JOIN dbo.Students s ON s.Id = par.StudentId
        INNER JOIN dbo.Classes c ON c.Id = par.ClassId
        LEFT JOIN dbo.Users u ON u.Id = par.MarkedBy
        LEFT JOIN dbo.TimetableSlots ts
          ON ts.ClassId = par.ClassId
         AND ts.Period = par.Period
         AND UPPER(LEFT(LTRIM(RTRIM(ts.[Day])), 3)) = UPPER(LEFT(DATENAME(WEEKDAY, par.[Date]), 3))
         AND LOWER(LTRIM(RTRIM(ts.Subject))) = LOWER(LTRIM(RTRIM(par.Subject)))
        LEFT JOIN dbo.Teachers t ON t.Id = ts.TeacherId
        WHERE par.[Date] >= @From
          AND par.[Date] <= @To
          AND (@ClassId IS NULL OR par.ClassId = @ClassId)
          AND (@Grade IS NULL OR c.Grade = @Grade)
          AND (@Section IS NULL OR c.Section = @Section)
          AND (@Subject IS NULL OR LOWER(LTRIM(RTRIM(par.Subject))) = LOWER(LTRIM(RTRIM(@Subject))))
          AND (@Period IS NULL OR par.Period = @Period)
          AND (@AssignedTeacherId IS NULL OR ts.TeacherId = @AssignedTeacherId)
          AND (@MarkedBy IS NULL OR par.MarkedBy = @MarkedBy)
          AND (@MarkedByRole IS NULL OR par.MarkedByRole = @MarkedByRole)
          AND (@Status IS NULL OR par.Status = @Status)
          AND (@Q IS NULL OR s.Name LIKE N'%' + @Q + N'%' OR s.AdmissionNo LIKE N'%' + @Q + N'%')
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
        parameters.Add("MarkedBy", q.MarkedBy);
        parameters.Add("MarkedByRole", Clean(q.MarkedByRole));
        parameters.Add("Status", Clean(q.Status));
        parameters.Add("Q", Clean(q.Q));
        parameters.Add("Offset", (page - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        return new PeriodAttendanceQueryCommand(Sql, parameters, page, pageSize);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
