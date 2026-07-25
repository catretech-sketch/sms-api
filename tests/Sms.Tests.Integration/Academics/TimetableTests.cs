using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class TimetableTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid tenantId, params string[] roles) =>
        ClientForUser(app, tenantId, Guid.NewGuid(), roles);

    private static HttpClient ClientForUser(
        WebApplicationFactory<Program> app, Guid tenantId, Guid userId, params string[] roles)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, roles, isPlatform: false);
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
    public async Task Principal_can_create_slot_and_teacher_can_list_it()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacherUserId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);
        var teacher = ClientForUser(app, tenantId, teacherUserId, Policies.Teacher);

        // Teacher must be linked (Teachers.UserId) and assigned to the subject the
        // slot is created for — /timetable now scopes teachers to their own slots.
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId) VALUES (@teacherUserId, @tenantId)", new { teacherUserId, tenantId });
            var teacherId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES (@teacherId, @tenantId, 'T1', @teacherUserId)",
                new { teacherId, tenantId, teacherUserId });
            await conn.ExecuteAsync(
                "INSERT dbo.Subjects (Id, TenantId, Name, TeacherId) VALUES (NEWID(), @tenantId, 'Mathematics', @teacherId)",
                new { tenantId, teacherId });
        }

        // POST as principal → 201
        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Mon", period = 1, subject = "Mathematics", room = "101",
            start_time = "08:00", end_time = "08:45"
        }), HttpStatusCode.Created);

        slot.GetProperty("day").GetString().Should().Be("Mon");
        slot.GetProperty("period").GetInt32().Should().Be(1);
        slot.GetProperty("subject").GetString().Should().Be("Mathematics");
        slot.GetProperty("room").GetString().Should().Be("101");
        var slotId = slot.GetProperty("id").GetGuid();

        // GET as teacher → 200, slot appears in list
        var list = await Data(await teacher.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        var found = false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == slotId) { found = true; break; }
        }
        found.Should().BeTrue("created slot should appear in the teacher's timetable list");
    }

    [Fact]
    public async Task StudentOrParent_gets_403_on_get_and_post()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var student = Client(app, tenantId, Policies.StudentOrParent);

        var getRes = await student.GetAsync("/v1/timetable");
        getRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var postRes = await student.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Tue", period = 2, subject = "Science"
        });
        postRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Teacher_gets_403_on_post()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacher = Client(app, tenantId, Policies.Teacher);

        var postRes = await teacher.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Wed", period = 3, subject = "English"
        });
        postRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
