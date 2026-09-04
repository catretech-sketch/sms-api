using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Dapper;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TripStopEndpointsTests(SqlServerFixture fx)
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static string IssueToken(Guid userId, Guid tenantId, string role) =>
        new JwtTokenService(new JwtOptions { SigningKey = Key }, new SystemClock())
            .IssueAccess(userId, tenantId, [role], isPlatform: false);

    private async Task<(Guid tenantId, Guid tripId, Guid driverId, Guid stop1, Guid stop2)> SeedLiveTripWithTwoStops()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stop1 = Guid.NewGuid();
        var stop2 = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo, DriverStaffId) VALUES (@Id, @TenantId, 'BUS-1', @DriverId)",
            new { Id = busId, TenantId = tenantId, DriverId = driverId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.RouteStops (Id, TenantId, RouteId, Name, Seq, Lat, Lng) VALUES
              (@S1, @TenantId, @RouteId, 'Stop A', 1, 12.1000, 77.1000),
              (@S2, @TenantId, @RouteId, 'Stop B', 2, 12.2000, 77.2000)",
            new { S1 = stop1, S2 = stop2, TenantId = tenantId, RouteId = routeId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Trips (Id, TenantId, BusId, RouteId, DriverId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, @RouteId, @DriverId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId, RouteId = routeId, DriverId = driverId });
        return (tenantId, tripId, driverId, stop1, stop2);
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> app, Guid userId, Guid tenantId, string role)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", IssueToken(userId, tenantId, role));
        return client;
    }

    [Fact]
    public async Task ConfirmArrival_out_of_order_stop_is_rejected()
    {
        var (tenantId, tripId, driverId, _, stop2) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/confirm-arrival", null);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConfirmArrival_then_Complete_advances_CurrentStopId_and_allows_the_next_stop()
    {
        var (tenantId, tripId, driverId, stop1, stop2) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var confirm1 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop1}/confirm-arrival", null);
        confirm1.IsSuccessStatusCode.Should().BeTrue();

        var completeBeforeConfirm2 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/complete", null);
        completeBeforeConfirm2.StatusCode.Should().Be(HttpStatusCode.Conflict, "stop2 was never confirmed as current");

        var complete1 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop1}/complete", null);
        complete1.IsSuccessStatusCode.Should().BeTrue();

        var confirm2 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/confirm-arrival", null);
        confirm2.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task SchoolArrived_sets_status_without_ending_the_trip()
    {
        var (tenantId, tripId, driverId, _, _) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/school-arrived", null);
        res.IsSuccessStatusCode.Should().BeTrue();

        // Trip must still accept a subsequent action (e.g. End) — proving it wasn't closed.
        var endRes = await client.PostAsync($"/v1/staff/trips/{tripId}/end", null);
        endRes.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task SchoolArrived_on_a_drop_trip_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo, DriverStaffId) VALUES (@Id, @TenantId, 'BUS-1', @DriverId)",
                new { Id = busId, TenantId = tenantId, DriverId = driverId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Trips (Id, TenantId, BusId, DriverId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, @DriverId, 'drop', 'live', SYSUTCDATETIME())",
                new { Id = tripId, TenantId = tenantId, BusId = busId, DriverId = driverId });
        }
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/school-arrived", null);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
