using System.Net;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Attendance;

[Collection("sql")]
public sealed class PeriodAttendanceAdvancedListTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";
    private static readonly DateTime Now = new(2026, 8, 13, 7, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Principal_can_list_period_records_using_clock_based_preset_and_marked_by_filter()
    {
        await using var app = App();
        var seed = await SeedAsync();
        var client = Client(app, Guid.NewGuid(), seed.TenantId, Policies.Principal);

        var response = await client.GetAsync(
            $"/v1/attendance/period-records?preset=this_month&markedBy={seed.MarkerAUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("total_count").GetInt32().Should().Be(1);
        data.GetProperty("items")[0].GetProperty("student_name").GetString().Should().Be("Student A");
        data.GetProperty("items")[0].GetProperty("marked_by").GetGuid().Should().Be(seed.MarkerAUserId);
    }

    [Fact]
    public async Task Teacher_is_restricted_to_their_assigned_periods_even_when_filter_requests_another_teacher()
    {
        await using var app = App();
        var seed = await SeedAsync();
        var client = Client(app, seed.TeacherAUserId, seed.TenantId, Policies.Teacher);

        var response = await client.GetAsync(
            $"/v1/attendance/period-records?from=2026-08-01&to=2026-08-31&assignedTeacherId={seed.TeacherBId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("total_count").GetInt32().Should().Be(1);
        data.GetProperty("items")[0].GetProperty("assigned_teacher_id").GetGuid().Should().Be(seed.TeacherAId);
    }

    [Fact]
    public async Task Student_or_parent_cannot_use_crm_period_records_endpoint()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = Client(app, Guid.NewGuid(), tenantId, Policies.StudentOrParent);

        var response = await client.GetAsync(
            "/v1/attendance/period-records?from=2026-08-01&to=2026-08-31");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClock(Now));
            });
        });

    private static HttpClient Client(
        WebApplicationFactory<Program> app,
        Guid userId,
        Guid tenantId,
        params string[] roles)
    {
        var jwt = new JwtTokenService(
            new JwtOptions
            {
                Issuer = "sms",
                Audience = "sms-apps",
                SigningKey = Key,
                AccessTokenMinutes = 15,
            },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, roles, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<Seed> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var classAId = Guid.NewGuid();
        var classBId = Guid.NewGuid();
        var studentAId = Guid.NewGuid();
        var studentBId = Guid.NewGuid();
        var teacherAId = Guid.NewGuid();
        var teacherBId = Guid.NewGuid();
        var teacherAUserId = Guid.NewGuid();
        var teacherBUserId = Guid.NewGuid();
        var markerAUserId = Guid.NewGuid();
        var markerBUserId = Guid.NewGuid();

        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId",
            new { tenantId });
        await conn.ExecuteAsync(
            """
            INSERT dbo.Users (Id, TenantId, Name) VALUES
                (@teacherAUserId, @tenantId, N'Teacher A User'),
                (@teacherBUserId, @tenantId, N'Teacher B User'),
                (@markerAUserId, @tenantId, N'Marker A'),
                (@markerBUserId, @tenantId, N'Marker B');
            INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES
                (@teacherAId, @tenantId, N'Teacher A', @teacherAUserId),
                (@teacherBId, @tenantId, N'Teacher B', @teacherBUserId);
            INSERT dbo.Classes (Id, TenantId, Name, Grade, Section, StudentCount) VALUES
                (@classAId, @tenantId, N'IX-A', N'IX', N'A', 1),
                (@classBId, @tenantId, N'IX-B', N'IX', N'B', 1);
            INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Grade, Section, Status) VALUES
                (@studentAId, @tenantId, N'A-001', N'Student A', N'IX', N'A', N'active'),
                (@studentBId, @tenantId, N'B-001', N'Student B', N'IX', N'B', N'active');
            INSERT dbo.TimetableSlots
                (TenantId, [Day], Period, Subject, ClassId, ClassName, StartTime, EndTime, TeacherId)
            VALUES
                (@tenantId, N'Wed', 1, N'Math', @classAId, N'IX-A', N'09:00', N'09:45', @teacherAId),
                (@tenantId, N'Wed', 1, N'Math', @classBId, N'IX-B', N'09:00', N'09:45', @teacherBId);
            INSERT dbo.PeriodAttendanceRecords
                (Id, TenantId, ClassId, StudentId, [Date], Period, Subject, Status, MarkedBy, MarkedByRole, CreatedAt, UpdatedAt)
            VALUES
                (NEWID(), @tenantId, @classAId, @studentAId, '2026-08-12', 1, N'Math', N'present', @markerAUserId, N'teacher', SYSUTCDATETIME(), SYSUTCDATETIME()),
                (NEWID(), @tenantId, @classBId, @studentBId, '2026-08-12', 1, N'Math', N'absent', @markerBUserId, N'teacher', SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new
            {
                tenantId,
                classAId,
                classBId,
                studentAId,
                studentBId,
                teacherAId,
                teacherBId,
                teacherAUserId,
                teacherBUserId,
                markerAUserId,
                markerBUserId,
            });

        return new Seed(
            tenantId,
            teacherAId,
            teacherBId,
            teacherAUserId,
            markerAUserId);
    }

    private sealed record Seed(
        Guid TenantId,
        Guid TeacherAId,
        Guid TeacherBId,
        Guid TeacherAUserId,
        Guid MarkerAUserId);

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
