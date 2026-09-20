using System.Text.Json;
using Sms.Application.DTOs.Users;
using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class RoleTemplateDao(IDbConnectionFactory factory) : BaseRepository(factory), IRoleTemplateDao
{
    public async Task<IReadOnlyList<RoleTemplateOverrideDto>> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<RoleTemplateRow>("dbo.RoleTemplate_Get", new { TenantId = tenantId }, ct);
        return rows.Select(r => new RoleTemplateOverrideDto(r.Role, r.Module, r.Cap, r.Effect)).ToList();
    }

    public Task SetAsync(Guid tenantId, IReadOnlyList<RoleTemplateOverrideDto> overrides, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(
            overrides.Select(o => new { role = o.Role, module = o.Module, cap = o.Cap, effect = o.Effect }));
        return ExecuteProcAsync("dbo.RoleTemplate_Set", new { TenantId = tenantId, Json = json }, ct);
    }

    private sealed record RoleTemplateRow(string Role, string Module, string Cap, string Effect);
}
