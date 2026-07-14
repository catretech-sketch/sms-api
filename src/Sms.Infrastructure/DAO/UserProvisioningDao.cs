using System.Data;
using Dapper;
using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class UserProvisioningDao(IDbConnectionFactory factory) : BaseRepository(factory), IUserProvisioningDao
{
    public async Task<Guid> CreateUserAsync(Guid tenantId, string? email, string? phone, bool isPlatform,
        string[] roles, CancellationToken ct = default)
    {
        var id = await QuerySingleProcAsync<Guid>("dbo.User_Create",
            new { TenantId = tenantId, Email = email, Phone = phone, IsPlatform = isPlatform }, ct);
        foreach (var role in roles)
            await ExecuteProcAsync("dbo.UserRole_Add", new { UserId = id, Role = role }, ct);
        return id;
    }

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
