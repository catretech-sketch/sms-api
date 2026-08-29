using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

/// The single POST /v1/ai/search endpoint wired on top of the already-tested AiSearchService
/// orchestrator. These tests only pin the HTTP-layer contract (auth, status-code mapping for
/// each AiSearchError.Code, JSON casing) -- the orchestrator's own behaviour (classification,
/// authorization clamping, per-intent handlers) is covered by AiSearchService/-handler tests.
[Collection("sql")]
public class AiSearchControllerTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient Admin(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, [Policies.SchoolAdmin], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        await using var app = App();
        var anon = app.CreateClient();

        var res = await anon.PostAsJsonAsync("/v1/ai/search", new { query = "Aaj kitne bachche aaye?" });

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tenant_without_the_AiSearch_feature_gets_a_feature_locked_response()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "silver");
        var admin = Admin(app, tenantId);

        var res = await admin.PostAsJsonAsync("/v1/ai/search", new { query = "Aaj kitne bachche aaye?" });
        var body = await res.Content.ReadAsStringAsync();

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FeatureNotEnabled");
    }

    [Fact]
    public async Task Empty_query_returns_a_400_style_InvalidRequest()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "gold");
        var admin = Admin(app, tenantId);

        var res = await admin.PostAsJsonAsync("/v1/ai/search", new { query = "" });
        var body = await res.Content.ReadAsStringAsync();

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("InvalidRequest");
    }
}
