using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Services.AiSearch;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

/// The single authorization choke point: every scope value must be re-derived from the
/// authenticated caller, and anything the LLM-extracted filters claimed beyond that scope
/// must be clamped away (never answered, never leaked as "exists but forbidden").
[Collection("sql")]
public class AiSearchAuthorizationServiceTests(SqlServerFixture fx)
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

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(expected, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private async Task Seed(Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=1");
        await work(conn);
    }

    private async Task<Guid> ParentUserId(string email, Guid tenantId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=1");
        return await conn.QuerySingleAsync<Guid>(
            """
            SELECT Id FROM dbo.Users
            WHERE TenantId = @tenantId
              AND LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@email)))
            """,
            new { email, tenantId });
    }

    /// Runs <paramref name="act"/> against a scope whose ambient ITenantContext is the caller,
    /// exactly as the request pipeline would have set it after JWT validation.
    private static async Task<AiAuthorizationResult> AsCaller(
        WebApplicationFactory<Program> app, Guid tenantId, Guid userId,
        Func<IAiSearchAuthorizationService, Task<AiAuthorizationResult>> act)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, userId, isPlatform: false);
        return await act(scope.ServiceProvider.GetRequiredService<IAiSearchAuthorizationService>());
    }

    [Fact]
    public async Task Parent_querying_an_unlinked_student_name_gets_no_match_not_another_childs_data()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var admin = Admin(app, tenantId);
        var parentEmail = $"dad{Guid.NewGuid():N}@home.test";

        var aisha = await Data(await admin.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-AI-{Guid.NewGuid():N}"[..20],
            name = "Aisha Khan",
            grade = "IV",
            section = "B",
            roll = 1,
            guardian_email = parentEmail,
        }), HttpStatusCode.Created);

        // Unrelated student in the same tenant — the parent must never resolve to this row.
        var rahul = await Data(await admin.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-RA-{Guid.NewGuid():N}"[..20],
            name = "Rahul Verma",
            grade = "V",
            section = "A",
            roll = 2,
            guardian_email = $"other{Guid.NewGuid():N}@home.test",
        }), HttpStatusCode.Created);

        var parentId = await ParentUserId(parentEmail, tenantId);

        var result = await AsCaller(app, tenantId, parentId, svc => svc.AuthorizeAsync(
            "StudentAttendance",
            new AiSearchFilters("Rahul", null, null, "today", false),
            [Policies.StudentOrParent]));

        result.Allowed.Should().BeTrue();
        result.ResultIntent.Should().Be("StudentAttendance");
        result.ResolvedStudentId.Should().BeNull();
        result.ClampedFilters.StudentName.Should().BeNull();
        result.AllowedChildStudentIds.Should().BeEquivalentTo([aisha.GetProperty("id").GetGuid()]);
        result.AllowedChildStudentIds.Should().NotContain(rahul.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Parent_querying_their_own_child_by_name_resolves_that_child()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var admin = Admin(app, tenantId);
        var parentEmail = $"dad{Guid.NewGuid():N}@home.test";

        var aisha = await Data(await admin.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-AI-{Guid.NewGuid():N}"[..20],
            name = "Aisha Khan",
            grade = "IV",
            section = "B",
            roll = 1,
            guardian_email = parentEmail,
        }), HttpStatusCode.Created);

        var parentId = await ParentUserId(parentEmail, tenantId);

        var result = await AsCaller(app, tenantId, parentId, svc => svc.AuthorizeAsync(
            "StudentAttendance",
            new AiSearchFilters("aisha", null, null, "today", false),
            [Policies.StudentOrParent]));

        result.Allowed.Should().BeTrue();
        result.ResolvedStudentId.Should().Be(aisha.GetProperty("id").GetGuid());
        result.ClampedFilters.StudentName.Should().Be("aisha");
    }

    [Fact]
    public async Task Teacher_querying_a_class_they_do_not_teach_has_the_class_filter_clamped_away()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacherUserId = Guid.NewGuid();

        await Seed(async conn =>
        {
            await conn.ExecuteAsync(
                "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            var teacherId = Guid.NewGuid();
            var classId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId) VALUES (@teacherUserId, @tenantId)",
                new { teacherUserId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES (@teacherId, @tenantId, N'Meena', @teacherUserId)",
                new { teacherId, tenantId, teacherUserId });
            await conn.ExecuteAsync(
                """
                INSERT dbo.Classes (Id, TenantId, Name, StudentCount, ClassTeacherId)
                VALUES (@classId, @tenantId, N'8A', 0, @teacherId)
                """,
                new { classId, tenantId, teacherId });
            await conn.ExecuteAsync(
                """
                INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, TeacherId)
                VALUES (@tenantId, 'Mon', 1, N'Math', @classId, N'8A', @teacherId)
                """,
                new { tenantId, classId, teacherId });
        });

        var result = await AsCaller(app, tenantId, teacherUserId, svc => svc.AuthorizeAsync(
            "ClassAttendance",
            new AiSearchFilters(null, "9B", "B", "today", false),
            [Policies.Teacher]));

        result.Allowed.Should().BeTrue();
        result.AllowedClassNames.Should().BeEquivalentTo(["8A"]);
        result.ClampedFilters.ClassName.Should().BeNull();
        result.ClampedFilters.Section.Should().BeNull();
    }

    [Fact]
    public async Task Teacher_querying_a_class_they_do_teach_keeps_the_class_filter()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacherUserId = Guid.NewGuid();

        await Seed(async conn =>
        {
            await conn.ExecuteAsync(
                "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            var teacherId = Guid.NewGuid();
            var classId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId) VALUES (@teacherUserId, @tenantId)",
                new { teacherUserId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES (@teacherId, @tenantId, N'Meena', @teacherUserId)",
                new { teacherId, tenantId, teacherUserId });
            await conn.ExecuteAsync(
                """
                INSERT dbo.Classes (Id, TenantId, Name, StudentCount, ClassTeacherId)
                VALUES (@classId, @tenantId, N'8A', 0, @teacherId)
                """,
                new { classId, tenantId, teacherId });
            await conn.ExecuteAsync(
                """
                INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, TeacherId)
                VALUES (@tenantId, 'Mon', 1, N'Math', @classId, N'8A', @teacherId)
                """,
                new { tenantId, classId, teacherId });
        });

        var result = await AsCaller(app, tenantId, teacherUserId, svc => svc.AuthorizeAsync(
            "ClassAttendance",
            new AiSearchFilters(null, "8a", "A", "today", false),
            [Policies.Teacher]));

        result.Allowed.Should().BeTrue();
        result.ClampedFilters.ClassName.Should().Be("8a");
        result.ClampedFilters.Section.Should().Be("A");
    }

    [Fact]
    public async Task Staff_role_is_denied_for_DashboardSummary()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();

        var result = await AsCaller(app, tenantId, Guid.NewGuid(), svc => svc.AuthorizeAsync(
            "DashboardSummary",
            new AiSearchFilters(null, null, null, "today", false),
            [Policies.Staff]));

        result.Allowed.Should().BeFalse();
        result.ResultIntent.Should().Be("Forbidden");
        result.ResolvedStudentId.Should().BeNull();
        result.AllowedChildStudentIds.Should().BeNull();
        result.AllowedClassNames.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_intent_is_denied()
    {
        await using var app = App();

        var result = await AsCaller(app, Guid.NewGuid(), Guid.NewGuid(), svc => svc.AuthorizeAsync(
            "DropAllTables",
            new AiSearchFilters(null, null, null, null, false),
            [Policies.SchoolAdmin]));

        result.Allowed.Should().BeFalse();
        result.ResultIntent.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Admin_filters_pass_through_unclamped()
    {
        await using var app = App();

        var result = await AsCaller(app, Guid.NewGuid(), Guid.NewGuid(), svc => svc.AuthorizeAsync(
            "ClassAttendance",
            new AiSearchFilters(null, "9B", "B", "today", false),
            [Policies.SchoolAdmin]));

        result.Allowed.Should().BeTrue();
        result.ClampedFilters.ClassName.Should().Be("9B");
        result.ClampedFilters.Section.Should().Be("B");
        result.AllowedClassNames.Should().BeNull();
    }
}
