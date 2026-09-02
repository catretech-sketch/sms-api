using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
        public Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add(tenantId);
            return Task.CompletedTask;
        }
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
}
