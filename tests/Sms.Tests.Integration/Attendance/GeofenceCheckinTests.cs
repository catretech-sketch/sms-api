using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Attendance;

[Collection("sql")]
public class GeofenceCheckinTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";
    private const double SchoolLat = 12.9716;
    private const double SchoolLng = 77.5946;

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient TeacherClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, ["teacher"], isPlatform: false);
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

    private static async Task SetSchoolLocation(HttpClient client) =>
        (await client.PutAsJsonAsync("/v1/me/attendance/school-location", new
        {
            lat = SchoolLat, lng = SchoolLng, radius_meters = 50, name = "Main Campus"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task Punch_inside_geofence_is_verified_and_appears_in_today()
    {
        await using var app = App();
        var client = TeacherClient(app, Guid.NewGuid(), Guid.NewGuid());
        await SetSchoolLocation(client);

        var day = await Data(await client.PostAsJsonAsync("/v1/me/attendance/punch", new
        {
            kind = "in", at = DateTime.UtcNow, lat = SchoolLat, lng = SchoolLng, accuracy_meters = 5
        }), HttpStatusCode.Created);

        var checkIn = day.GetProperty("check_in");
        checkIn.ValueKind.Should().NotBe(JsonValueKind.Null);
        checkIn.GetProperty("verified").GetBoolean().Should().BeTrue();
        checkIn.GetProperty("distance_meters").GetDouble().Should().BeLessThan(50);

        var today = await Data(await client.GetAsync("/v1/me/attendance/today"), HttpStatusCode.OK);
        today.GetProperty("check_in").GetProperty("verified").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Punch_far_from_school_is_not_verified()
    {
        await using var app = App();
        var client = TeacherClient(app, Guid.NewGuid(), Guid.NewGuid());
        await SetSchoolLocation(client);

        // ~1.1 km away (0.01 degrees latitude)
        var day = await Data(await client.PostAsJsonAsync("/v1/me/attendance/punch", new
        {
            kind = "in", at = DateTime.UtcNow, lat = SchoolLat + 0.01, lng = SchoolLng, accuracy_meters = 5
        }), HttpStatusCode.Created);

        var checkIn = day.GetProperty("check_in");
        checkIn.GetProperty("verified").GetBoolean().Should().BeFalse();
        checkIn.GetProperty("distance_meters").GetDouble().Should().BeGreaterThan(500);
    }
}
