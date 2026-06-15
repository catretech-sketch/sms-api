using System.Data;
using Dapper;
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed record ImportRow(string? Email, string? Phone, string? Role);
public sealed record ImportResult(int Created, int Skipped);

public sealed class UserProvisioningRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    /// Creates one login-ready user (Status='active', no password — logs in via OTP) and its roles.
    public async Task<Guid> CreateUserAsync(Guid? tenantId, string? email, string? phone,
        bool isPlatform, IEnumerable<string> roles, CancellationToken ct = default)
    {
        var id = await QuerySingleProcAsync<Guid>("dbo.User_Create",
            new { TenantId = tenantId, Email = email, Phone = phone, IsPlatform = isPlatform }, ct);
        foreach (var role in roles)
            await ExecuteProcAsync("dbo.UserRole_Add", new { UserId = id, Role = role }, ct);
        return id;
    }

    /// Bulk-creates login users + roles in one TVP round-trip; skips duplicate email/phone in-tenant.
    public async Task<ImportResult> BulkCreateAsync(Guid tenantId, IReadOnlyList<ImportRow> rows,
        CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("Role", typeof(string));
        foreach (var r in rows)
            table.Rows.Add((object?)r.Email ?? DBNull.Value, (object?)r.Phone ?? DBNull.Value,
                (object?)r.Role ?? DBNull.Value);

        var p = new DynamicParameters();
        p.Add("@TenantId", tenantId);
        p.Add("@Rows", table.AsTableValuedParameter("dbo.UsersTvp"));

        var result = await QuerySingleProcAsync<ImportResult>("dbo.Users_BulkCreate", p, ct);
        return result ?? new ImportResult(0, rows.Count);
    }
}
