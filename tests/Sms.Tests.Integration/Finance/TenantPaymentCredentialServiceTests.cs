using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class TenantPaymentCredentialServiceTests(SqlServerFixture fx)
{
    private static ITenantPaymentCredentialService BuildService(SqlServerFixture fx)
    {
        var ctx = new TenantContext();
        ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var repo = new TenantPaymentCredentialRepository(factory);
        var provider = new ServiceCollection().AddDataProtection().Services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>();
        return new TenantPaymentCredentialService(repo, protector);
    }

    [Fact]
    public async Task Upsert_then_GetActive_round_trips_the_secret_decrypted()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);

        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            KeyId: "rzp_test_abc", KeySecret: "top-secret-value", WebhookSecret: "webhook-secret-value",
            Mode: "test", IsEnabled: true), CancellationToken.None);

        var active = await svc.GetActiveAsync(tenantId, CancellationToken.None);
        active.Should().NotBeNull();
        active!.KeyId.Should().Be("rzp_test_abc");
        active.KeySecret.Should().Be("top-secret-value");
        active.WebhookSecret.Should().Be("webhook-secret-value");
        active.Mode.Should().Be("test");

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var storedSecret = await conn.QuerySingleAsync<string>(
            "SELECT KeySecretEncrypted FROM dbo.TenantPaymentCredentials WHERE TenantId = @tenantId", new { tenantId });
        storedSecret.Should().NotBe("top-secret-value"); // must be encrypted at rest, not plaintext
    }

    [Fact]
    public async Task GetActive_returns_null_when_disabled()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_x", "secret", "whsecret", "test", IsEnabled: false), CancellationToken.None);

        (await svc.GetActiveAsync(tenantId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetActive_returns_null_when_never_configured()
    {
        var svc = BuildService(fx);
        (await svc.GetActiveAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_omitting_secret_leaves_stored_secret_unchanged()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_x", "original-secret", "original-webhook", "test", true), CancellationToken.None);

        // Second upsert changes only KeyId, omits both secrets (null = "leave unchanged")
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_y", null, null, "test", true), CancellationToken.None);

        var active = await svc.GetActiveAsync(tenantId, CancellationToken.None);
        active!.KeyId.Should().Be("rzp_test_y");
        active.KeySecret.Should().Be("original-secret");
        active.WebhookSecret.Should().Be("original-webhook");
    }
}
