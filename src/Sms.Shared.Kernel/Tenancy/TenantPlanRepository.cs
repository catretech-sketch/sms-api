using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantPlanRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public sealed record TierStatus(string? Tier, string? Status);

    public async Task<TierStatus?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<TierStatus>("dbo.Tenant_GetTierAndStatus",
            new { TenantId = tenantId }, ct);
        return rows.Count == 0 ? null : rows[0];
    }
}
