using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Transport;

/// A driver must only be able to mutate their OWN trip. These tests prove that a second driver in the
/// SAME tenant (so RLS alone does not block them) is rejected with 403 on ping/end/boarding.
[Collection("sql")]
public class TripOwnershipTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

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
    public async Task Peer_driver_in_same_tenant_cannot_mutate_anothers_trip()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var driver1 = StaffClient(app, tenantId, Guid.NewGuid());
        var driver2 = StaffClient(app, tenantId, Guid.NewGuid());

        // Driver 1 owns the trip.
        var trip = await Data(await driver1.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = "KA-01-F-9001" }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        var now = DateTime.UtcNow;

        // Driver 2 (same tenant, different user) is forbidden on every mutation.
        (await driver2.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 12.9, lng = 77.5, speed_kmh = 20, heading = 10, at = now } }
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await driver2.PostAsJsonAsync($"/v1/staff/trips/{tripId}/boarding",
            new { student_id = Guid.NewGuid(), state = "boarded", at = now }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await driver2.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner can still end their own trip.
        (await driver1.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
