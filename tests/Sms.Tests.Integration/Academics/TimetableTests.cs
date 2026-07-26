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
    public async Task Principal_list_includes_teacher_name_derived_from_the_slots_own_teacher_id()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);
        Guid teacherId;

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            teacherId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name) VALUES (@teacherId, @tenantId, 'Asha Rao')",
                new { teacherId, tenantId });
        }

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Tue", period = 2, subject = "Science", teacher_id = teacherId
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var list = await Data(await principal.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        var item = list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == slotId);
        item.GetProperty("teacher_name").GetString().Should().Be("Asha Rao");
    }

    [Fact]
    public async Task Slots_own_teacher_id_wins_over_the_subjects_default_teacher()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);
        Guid slotTeacherId;

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            var subjectDefaultTeacherId = Guid.NewGuid();
            slotTeacherId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name) VALUES (@subjectDefaultTeacherId, @tenantId, 'Default Teacher')",
                new { subjectDefaultTeacherId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name) VALUES (@slotTeacherId, @tenantId, 'Period Teacher')",
                new { slotTeacherId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Subjects (Id, TenantId, Name, TeacherId) VALUES (NEWID(), @tenantId, 'Science', @subjectDefaultTeacherId)",
                new { tenantId, subjectDefaultTeacherId });
        }

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Wed", period = 4, subject = "Science", teacher_id = slotTeacherId
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var list = await Data(await principal.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        var item = list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == slotId);
        item.GetProperty("teacher_name").GetString().Should().Be("Period Teacher");
    }

    [Fact]
    public async Task Falls_back_to_the_subjects_default_teacher_when_the_slot_has_no_teacher_id()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            var teacherId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name) VALUES (@teacherId, @tenantId, 'Asha Rao')",
                new { teacherId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Subjects (Id, TenantId, Name, TeacherId) VALUES (NEWID(), @tenantId, 'Science', @teacherId)",
                new { tenantId, teacherId });
        }

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Tue", period = 2, subject = "Science"
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var list = await Data(await principal.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        var item = list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == slotId);
        item.GetProperty("teacher_name").GetString().Should().Be("Asha Rao");
    }

    [Fact]
    public async Task Principal_list_leaves_teacher_name_null_when_subject_has_no_assigned_teacher()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Wed", period = 3, subject = "Unassigned Subject"
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var list = await Data(await principal.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        var item = list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == slotId);
        item.GetProperty("teacher_name").ValueKind.Should().Be(JsonValueKind.Null);
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

    [Fact]
    public async Task Principal_can_create_then_delete_a_slot()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Thu", period = 4, subject = "History"
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var del = await principal.DeleteAsync($"/v1/timetable/{slotId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await Data(await principal.GetAsync("/v1/timetable"), HttpStatusCode.OK);
        foreach (var item in list.EnumerateArray())
            item.GetProperty("id").GetGuid().Should().NotBe(slotId);
    }

    [Fact]
    public async Task Teacher_gets_403_on_delete()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var principal = Client(app, tenantId, Policies.Principal);
        var teacher = Client(app, tenantId, Policies.Teacher);

        var slot = await Data(await principal.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Fri", period = 5, subject = "Geography"
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var del = await teacher.DeleteAsync($"/v1/timetable/{slotId}");
        del.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_a_slot_from_another_tenant_returns_404()
    {
        await using var app = App();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var principalA = Client(app, tenantA, Policies.Principal);
        var principalB = Client(app, tenantB, Policies.Principal);

        var slot = await Data(await principalA.PostAsJsonAsync("/v1/timetable", new
        {
            day = "Mon", period = 6, subject = "Art"
        }), HttpStatusCode.Created);
        var slotId = slot.GetProperty("id").GetGuid();

        var del = await principalB.DeleteAsync($"/v1/timetable/{slotId}");
        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
