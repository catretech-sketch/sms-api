using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class CalendarTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid tenantId, params string[] roles)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, roles, isPlatform: false);
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Principal_can_create_event_and_teacher_can_list_it()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);
        var teacher = Client(app, tenantId, Policies.Teacher);

        // POST as principal → 201
        var ev = await Data(await principal.PostAsJsonAsync("/v1/calendar", new
        {
            title = "Sports Day",
            date = "2025-03-15",
            type = "event",
            description = "Annual sports day celebration"
        }), HttpStatusCode.Created);

        ev.GetProperty("title").GetString().Should().Be("Sports Day");
        ev.GetProperty("type").GetString().Should().Be("event");
        ev.GetProperty("description").GetString().Should().Be("Annual sports day celebration");
        var eventId = ev.GetProperty("id").GetGuid();

        // GET as teacher → 200, event appears in list
        var list = await Data(await teacher.GetAsync("/v1/calendar"), HttpStatusCode.OK);
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        var found = false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == eventId) { found = true; break; }
        }
        found.Should().BeTrue("created event should appear in the teacher's calendar list");
    }

    [Fact]
    public async Task Teacher_gets_403_on_post()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacher = Client(app, tenantId, Policies.Teacher);

        var postRes = await teacher.PostAsJsonAsync("/v1/calendar", new
        {
            title = "Holiday",
            date = "2025-04-01",
            type = "holiday"
        });
        postRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StudentOrParent_gets_403_on_get_and_post()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var student = Client(app, tenantId, Policies.StudentOrParent);

        var getRes = await student.GetAsync("/v1/calendar");
        getRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var postRes = await student.PostAsJsonAsync("/v1/calendar", new
        {
            title = "Test Event",
            date = "2025-05-01",
            type = "event"
        });
        postRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
