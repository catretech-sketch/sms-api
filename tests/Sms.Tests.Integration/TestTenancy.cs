using Dapper;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration;

/// <summary>Seed dbo.Tenants for integration tests that need a plan tier.</summary>
public static class TestTenancy
{
    public static async Task EnsureTenantAsync(string connectionString, Guid tenantId,
        string tier = "silver", string status = "active")
    {
        var ctx = new TenantContext();
        ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(connectionString, ctx);
        await using var conn = await factory.OpenAsync();
        await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @tenantId)
    INSERT dbo.Tenants (Id, Name, Slug, Status, Tier)
    VALUES (@tenantId, @name, @slug, @status, @tier)
ELSE
    UPDATE dbo.Tenants SET Tier = @tier, Status = @status WHERE Id = @tenantId",
            new
            {
                tenantId,
                name = "Integration Test",
                slug = $"t{tenantId:N}",
                status,
                tier,
            });
    }
}
