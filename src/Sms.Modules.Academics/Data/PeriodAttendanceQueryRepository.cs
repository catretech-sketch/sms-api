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
               CAST(N'not_required' AS nvarchar(32)) AS GeoFenceStatus
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
        parameters.Add("Q", Clean(q.Q));
        parameters.Add("Offset", (page - 1L) * pageSize);
        parameters.Add("PageSize", pageSize);

        return new PeriodAttendanceQueryCommand(Sql, parameters, page, pageSize);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
