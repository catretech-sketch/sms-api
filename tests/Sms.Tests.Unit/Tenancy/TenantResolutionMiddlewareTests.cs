using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task Populates_context_from_jwt_tenant_and_sub_claims()
    {
        var ctx = new TenantContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("is_platform", "0"),
        }, "test"));
        http.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(http, ctx);

        ctx.TenantId.Should().Be(tenantId);
        ctx.UserId.Should().Be(userId);
        ctx.IsPlatform.Should().BeFalse();
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
        await mw.InvokeAsync(http, ctx);

        http.Response.StatusCode.Should().Be(403);
        called.Should().BeFalse();
    }
}
