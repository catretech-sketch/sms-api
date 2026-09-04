using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Sms.Application.Services.Realtime;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Transport;

/// The driver-facing /staff/trips lifecycle must push the same live signals the admin/teacher
/// bus-duty lifecycle already does, so a fleet view or live map actually updates in real time
/// instead of relying on polling.
[Collection("sql")]
public class TripBroadcastTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private sealed class SpyFleetBroadcaster : ITransportFleetBroadcaster
    {
        public List<Guid> Calls { get; } = [];
        public List<(Guid BusId, Sms.Modules.Transport.BusLiveSnapshotResponse Snapshot)> PositionCalls { get; } = [];
        public List<(Guid BusId, Guid TripId, Guid? DriverId, Guid? ConductorId, string Direction, DateTime StartedAt)> TripStartedCalls { get; } = [];
        public List<(Guid BusId, Guid TripId, DateTime EndedAt)> TripEndedCalls { get; } = [];
        public Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add(tenantId);
            return Task.CompletedTask;
        }
        public Task BroadcastPositionAsync(Guid busId, Sms.Modules.Transport.BusLiveSnapshotResponse snapshot, CancellationToken ct = default)
        {
            PositionCalls.Add((busId, snapshot));
            return Task.CompletedTask;
        }
        public Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default)
        {
            TripStartedCalls.Add((busId, tripId, driverId, conductorId, direction, startedAt));
            return Task.CompletedTask;
        }
        public Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default)
        {
            TripEndedCalls.Add((busId, tripId, endedAt));
            return Task.CompletedTask;
        }
        public Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SpyLiveBroadcaster : ILiveBroadcaster
    {
        public List<(Guid TenantId, string Type)> Calls { get; } = [];
        public Task PublishAsync(Guid tenantId, string type, object? data = null, CancellationToken ct = default)
        {
            Calls.Add((tenantId, type));
            return Task.CompletedTask;
        }
    }

    private (WebApplicationFactory<Program> App, SpyFleetBroadcaster Fleet, SpyLiveBroadcaster Live) App()
    {
        var fleet = new SpyFleetBroadcaster();
        var live = new SpyLiveBroadcaster();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services =>
            {
                services.AddScoped<ITransportFleetBroadcaster>(_ => fleet);
                services.AddScoped<ILiveBroadcaster>(_ => live);
            });
        });
        return (app, fleet, live);
    }

    private static HttpClient StaffClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, ["driver"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Starting_pinging_and_ending_a_trip_each_broadcast_live_updates()
    {
        var (app, fleet, live) = App();
        await using var _ = app;
        var tenantId = Guid.NewGuid();
        var client = StaffClient(app, tenantId, Guid.NewGuid());

        var start = await client.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = "KA-01-F-3301" });
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var tripId = (await start.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        fleet.Calls.Should().ContainSingle().Which.Should().Be(tenantId);
        live.Calls.Should().ContainSingle(c => c.TenantId == tenantId && c.Type == LiveEventTypes.Transport);

        var now = DateTime.UtcNow;
        (await client.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 12.9716, lng = 77.5946, speed_kmh = 20, heading = 10, at = now } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        fleet.Calls.Should().HaveCount(2);
        live.Calls.Should().HaveCount(2);

        (await client.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        fleet.Calls.Should().HaveCount(3);
        live.Calls.Should().HaveCount(3);
        fleet.Calls.Should().AllSatisfy(t => t.Should().Be(tenantId));
    }

    [Fact]
    public async Task ActiveBroadcaster_reflects_the_most_recently_pinging_role()
    {
        var (app, _, _) = App();
        await using var _dispose = app;
        var tenantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var conductorUserId = Guid.NewGuid();
        var conductorStaffId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Staff (Id, TenantId, Name, UserId) VALUES (@Id, @TenantId, @Name, @UserId)",
                new { Id = conductorStaffId, TenantId = tenantId, Name = "Priya Rao", UserId = conductorUserId });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo, ConductorStaffId) VALUES (@Id, @TenantId, @BusNo, @ConductorStaffId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, BusNo = busNo, ConductorStaffId = conductorStaffId });
        }

        var driver = StaffClient(app, tenantId, driverUserId);
        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = busNo }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        var now = DateTime.UtcNow;
        (await driver.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 1.0, lng = 1.0, speed_kmh = 10, heading = 0, at = now } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var current = await Data(await driver.GetAsync("/v1/staff/trip/current"), HttpStatusCode.OK);
        current.GetProperty("active_broadcaster").GetString().Should().Be("driver");
    }

    /// Starting, pinging, and ending a trip that is tied to a real bus row must also push the
    /// per-bus SignalR events (trip_started/position_update/trip_ended) that a live map or the
    /// bus's own group subscribers rely on, in addition to the tenant-wide fleet snapshot.
    [Fact]
    public async Task Starting_pinging_and_ending_a_trip_broadcasts_the_buss_lifecycle_events()
    {
        var (app, fleet, _) = App();
        await using var _dispose = app;
        var tenantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, @BusNo)",
                new { Id = busId, TenantId = tenantId, BusNo = busNo });
        }

        var driver = StaffClient(app, tenantId, driverUserId);
        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = busNo }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        fleet.TripStartedCalls.Should().ContainSingle(c => c.BusId == busId && c.TripId == tripId && c.Direction == "pickup");

        var now = DateTime.UtcNow;
        (await driver.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 12.9716, lng = 77.5946, speed_kmh = 20, heading = 10, at = now } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        fleet.PositionCalls.Should().ContainSingle(c => c.BusId == busId);

        (await driver.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        fleet.TripEndedCalls.Should().ContainSingle(c => c.BusId == busId && c.TripId == tripId);
    }
}
