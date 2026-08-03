using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class ProfileDao(IDbConnectionFactory factory) : BaseRepository(factory), IProfileDao
{
    private const string TeacherSelect =
        """
        SELECT TOP 1 t.Designation, t.ClassTeacher, t.Phone, t.Email, t.EmployeeCode, t.CreatedAt AS JoinedAt,
            (SELECT TOP 1 c.Name FROM dbo.Classes c WHERE c.ClassTeacherId = t.Id) AS HomeroomClassName
        FROM dbo.Teachers t
        WHERE t.UserId = @userId
           OR (
                @tenantId IS NOT NULL AND t.TenantId = @tenantId AND (
                    (@email IS NOT NULL AND LOWER(LTRIM(RTRIM(t.Email))) = LOWER(LTRIM(RTRIM(@email))))
                    OR (@name IS NOT NULL AND LOWER(LTRIM(RTRIM(t.Name))) = LOWER(LTRIM(RTRIM(@name))))
                )
              )
        ORDER BY CASE WHEN t.UserId = @userId THEN 0
                      WHEN @email IS NOT NULL AND LOWER(LTRIM(RTRIM(t.Email))) = LOWER(LTRIM(RTRIM(@email))) THEN 1
                      ELSE 2 END, t.CreatedAt
        """;

    private const string StaffSelect =
        """
        SELECT TOP 1 s.Role AS Designation,
            CAST(NULL AS nvarchar(40)) AS ClassTeacher,
            s.Phone, s.Email, s.EmployeeCode,
            s.CreatedAt AS JoinedAt,
            CAST(NULL AS nvarchar(200)) AS HomeroomClassName
        FROM dbo.Staff s
        WHERE s.UserId = @userId
           OR (
                @tenantId IS NOT NULL AND s.TenantId = @tenantId AND (
                    (@email IS NOT NULL AND LOWER(LTRIM(RTRIM(s.Email))) = LOWER(LTRIM(RTRIM(@email))))
                    OR (@name IS NOT NULL AND LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(@name))))
                )
              )
        ORDER BY CASE WHEN s.UserId = @userId THEN 0
                      WHEN @email IS NOT NULL AND LOWER(LTRIM(RTRIM(s.Email))) = LOWER(LTRIM(RTRIM(@email))) THEN 1
                      ELSE 2 END, s.CreatedAt
        """;

    public Task<LinkedPersonProfile?> GetLinkedTeacherAsync(
        Guid userId, Guid? tenantId, string? email, string? name, CancellationToken ct = default) =>
        QuerySingleOrDefaultAsync(tenantId, email, name, userId, TeacherSelect, ct);

    public Task<LinkedPersonProfile?> GetLinkedStaffAsync(
        Guid userId, Guid? tenantId, string? email, string? name, CancellationToken ct = default) =>
        QuerySingleOrDefaultAsync(tenantId, email, name, userId, StaffSelect, ct);

    public async Task<string?> GetSharedPhoneFromRosterAsync(string? email, string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(name)) return null;
        var rows = await QueryInlineAsync<string>(
            """
            SELECT TOP 1 Phone FROM (
                SELECT t.Phone, t.CreatedAt
                FROM dbo.Teachers t
                WHERE t.Phone IS NOT NULL AND LTRIM(t.Phone) <> ''
                  AND (
                    (@email IS NOT NULL AND t.Email IS NOT NULL
                     AND LOWER(LTRIM(RTRIM(t.Email))) = LOWER(LTRIM(RTRIM(@email))))
                    OR (@name IS NOT NULL AND LOWER(LTRIM(RTRIM(t.Name))) = LOWER(LTRIM(RTRIM(@name))))
                  )
                UNION ALL
                SELECT s.Phone, s.CreatedAt
                FROM dbo.Staff s
                WHERE s.Phone IS NOT NULL AND LTRIM(s.Phone) <> ''
                  AND (
                    (@email IS NOT NULL AND s.Email IS NOT NULL
                     AND LOWER(LTRIM(RTRIM(s.Email))) = LOWER(LTRIM(RTRIM(@email))))
                    OR (@name IS NOT NULL AND LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(@name))))
                  )
            ) AS contacts
            ORDER BY CreatedAt DESC
            """,
            new { email, name }, ct);
        return rows.FirstOrDefault()?.Trim();
    }

    private async Task<LinkedPersonProfile?> QuerySingleOrDefaultAsync(
        Guid? tenantId, string? email, string? name, Guid userId, string sql, CancellationToken ct)
    {
        var rows = await QueryInlineAsync<LinkedPersonProfile>(
            sql, new { userId, tenantId, email, name }, ct);
        return rows.FirstOrDefault();
    }
}
