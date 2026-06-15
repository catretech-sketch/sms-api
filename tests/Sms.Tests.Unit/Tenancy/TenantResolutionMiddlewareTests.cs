using System.Data;
using System.Data.Common;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class TenantResolutionMiddlewareTests
{
    // Stub ITenantPlan for unit tests (no DB needed).
    private sealed class StubPlan : ITenantPlan
    {
        public Guid? TenantId { get; private set; }
        public string Tier { get; private set; } = "";
        public string Status { get; private set; } = "";
        public void Set(Guid? tenantId, string tier, string status)
        {
            TenantId = tenantId; Tier = tier ?? ""; Status = status ?? "";
        }
    }

    // A no-op IDbConnectionFactory for unit tests — never actually called when IsPlatform=true.
    private sealed class NullDbConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> OpenAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("No DB in unit tests.");
    }

    private static TenantPlanRepository NullRepo() => new(new NullDbConnectionFactory());

    [Fact]
    public async Task Populates_context_from_jwt_tenant_and_sub_claims()
    {
        var ctx = new TenantContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        // Use isPlatform=true so planRepo is not invoked (no DB in unit tests).
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("is_platform", "1"),
        }, "test"));
        http.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(http, ctx, new StubPlan(), NullRepo());

        ctx.TenantId.Should().Be(tenantId);
        ctx.UserId.Should().Be(userId);
        ctx.IsPlatform.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_mismatch_between_header_and_token_tenant()
    {
        var ctx = new TenantContext();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
        }, "test"));
        http.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString(); // different tenant

        var called = false;
        var mw = new TenantResolutionMiddleware(_ => { called = true; return Task.CompletedTask; });
        // isPlatform defaults to false (no is_platform claim) — early 403 exit, planRepo not invoked.
        await mw.InvokeAsync(http, ctx, new StubPlan(), NullRepo());

        http.Response.StatusCode.Should().Be(403);
        called.Should().BeFalse();
    }
}
