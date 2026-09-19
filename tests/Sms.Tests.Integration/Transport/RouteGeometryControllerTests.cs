using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Routing;
using Sms.Shared.Kernel.Time;
using Sms.Tests.Integration;

namespace Sms.Tests.Integration.Transport;

/// Covers the shared /v1/transport/routes/{routeId}/geometry endpoint: any authenticated caller
/// reaches the action ([Authorize] with no policy), but real access control is the per-route
/// CanViewRouteAsync check inside it (see RouteGeometryController for why this can't just be a
/// TransportController action). These tests are also the empirical proof that Task 8's DI wiring
/// for IRouteGeometryService/IGoogleRoutesClient/ITransportAuthorizationResolver actually resolves
/// at runtime through the real ASP.NET Core host, not just via a manual trace.
[Collection("sql")]
public class RouteGeometryControllerTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private sealed class FakeGoogleRoutesClient(ComputedRouteGeometry? result) : IGoogleRoutesClient
    {
        public Task<ComputedRouteGeometry?> ComputeRouteAsync(
            IReadOnlyList<RouteWaypoint> orderedWaypoints, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private static WebApplicationFactory<Program> App(string connectionString, IGoogleRoutesClient? fakeRoutesClient = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", connectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            if (fakeRoutesClient is not null)
                b.ConfigureTestServices(services => services.AddScoped(_ => fakeRoutesClient));
        });

    private static HttpClient ClientFor(WebApplicationFactory<Program> app, Guid userId, Guid tenantId, string role)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, [role], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task Seed(string cs, Guid tenantId, Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await work(conn);
    }

    [Fact]
    public async Task Principal_gets_available_geometry_for_own_tenant_route()
    {
        var tenantId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@Id, @TenantId, 'ROUTE-GEO-1')",
                new { Id = routeId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.RouteStops (Id, TenantId, RouteId, Name, Seq, Lat, Lng) VALUES " +
                "(@Id1, @TenantId, @RouteId, 'Stop A', 1, 12.9716, 77.5946), " +
                "(@Id2, @TenantId, @RouteId, 'Stop B', 2, 12.9816, 77.6046)",
                new { Id1 = Guid.NewGuid(), Id2 = Guid.NewGuid(), TenantId = tenantId, RouteId = routeId });
        });

        var fixedGeometry = new ComputedRouteGeometry("fake-polyline-abc123", 4200, 900);
        await using var app = App(fx.ConnectionString, new FakeGoogleRoutesClient(fixedGeometry));
        var client = ClientFor(app, principalId, tenantId, Policies.Principal);

        var res = await client.GetAsync($"/v1/transport/routes/{routeId}/geometry");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("available");
        data.GetProperty("geometry").GetString().Should().Be("fake-polyline-abc123");
        data.GetProperty("distance_meters").GetInt32().Should().Be(4200);
        data.GetProperty("duration_seconds").GetInt32().Should().Be(900);
    }

    [Fact]
    public async Task Teacher_without_bus_duty_on_the_route_gets_403()
    {
        var tenantId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var uninvolvedTeacherId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@Id, @TenantId, 'ROUTE-GEO-2')",
                new { Id = routeId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo, RouteId) VALUES (@Id, @TenantId, 'BUS-GEO-1', @RouteId)",
                new { Id = busId, TenantId = tenantId, RouteId = routeId });
            // Deliberately no BusAssignments/BusTravelingTeachers/Trips row links this teacher to
            // the bus on the route — they must be denied.
        });

        await using var app = App(fx.ConnectionString);
        var client = ClientFor(app, uninvolvedTeacherId, tenantId, Policies.Teacher);

        var res = await client.GetAsync($"/v1/transport/routes/{routeId}/geometry");

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Route_belonging_to_another_tenant_is_not_visible()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var principalOfB = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantA, tier: "platinum");
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantB, tier: "platinum");

        await Seed(fx.ConnectionString, tenantA, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@Id, @TenantId, 'ROUTE-GEO-3')",
                new { Id = routeId, TenantId = tenantA });
        });

        await using var app = App(fx.ConnectionString);
        var client = ClientFor(app, principalOfB, tenantB, Policies.Principal);

        var res = await client.GetAsync($"/v1/transport/routes/{routeId}/geometry");

        // RLS makes RouteExistsAsync return false for tenant B's session -> 403 (not 404, since the
        // endpoint doesn't leak existence).
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_stops_or_provider_unavailable_returns_200_with_unavailable_status()
    {
        var tenantId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@Id, @TenantId, 'ROUTE-GEO-4')",
                new { Id = routeId, TenantId = tenantId });
            // Deliberately zero RouteStops rows — the service short-circuits to Unavailable
            // before ever calling IGoogleRoutesClient.
        });

        await using var app = App(fx.ConnectionString);
        var client = ClientFor(app, principalId, tenantId, Policies.Principal);

        var res = await client.GetAsync($"/v1/transport/routes/{routeId}/geometry");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("unavailable");
        data.GetProperty("geometry").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
