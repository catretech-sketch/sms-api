using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

/// Resolves AiAttendanceAggregateRepository directly (no HTTP layer needed yet — the AI search
/// controller lands in Task 12) and asserts SchoolWideAsync only counts the authenticated
/// tenant's active students marked today, never another tenant's rows.
[Collection("sql")]
public class DailyAttendanceSummaryHandlerTests(SqlServerFixture fx)
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
        var jwt = new Sms.Shared.Kernel.Auth.JwtTokenService(
            new Sms.Shared.Kernel.Auth.JwtOptions
            {
                Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15,
            },
            new Sms.Shared.Kernel.Time.SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.admin"], isPlatform: false);
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

    private static async Task MarkPresent(
        SqlConnection conn, Guid tenantId, Guid studentId, DateOnly date, string status)
    {
        await conn.ExecuteAsync(
            """
            INSERT dbo.PeriodAttendanceRecords
                (Id, TenantId, ClassId, StudentId, [Date], Period, Subject, Status)
            VALUES
                (NEWID(), @tenantId, @classId, @studentId, @date, 1, N'Math', @status)
            """,
            new { tenantId, classId = Guid.NewGuid(), studentId, date = date.ToDateTime(TimeOnly.MinValue), status });
    }

    /// Runs <paramref name="act"/> against a scope whose ambient ITenantContext matches the tenant
    /// being queried, exactly as the request pipeline would have set it after JWT validation.
    private static async Task<AttendanceAggregate> AsTenant(
        WebApplicationFactory<Program> app, Guid tenantId,
        Func<AiAttendanceAggregateRepository, Task<AttendanceAggregate>> act)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, Guid.NewGuid(), isPlatform: false);
        return await act(scope.ServiceProvider.GetRequiredService<AiAttendanceAggregateRepository>());
    }

    [Fact]
    public async Task Aggregate_counts_only_the_authenticated_tenants_active_students()
    {
        await using var app = App();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var adminA = Admin(app, tenantA);
        var adminB = Admin(app, tenantB);

        // Tenant A: 2 active students, one present, one absent today.
        var a1 = await Data(await adminA.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-A1-{Guid.NewGuid():N}"[..20],
            name = "Tenant A Present",
            grade = "IV",
            section = "B",
            roll = 1,
        }), HttpStatusCode.Created);
        var a2 = await Data(await adminA.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-A2-{Guid.NewGuid():N}"[..20],
            name = "Tenant A Absent",
            grade = "IV",
            section = "B",
            roll = 2,
        }), HttpStatusCode.Created);

        // Tenant B: 1 active student, present today — must never be counted for tenant A.
        var b1 = await Data(await adminB.PostAsJsonAsync("/v1/students", new
        {
            admission_no = $"ADM-B1-{Guid.NewGuid():N}"[..20],
            name = "Tenant B Present",
            grade = "IV",
            section = "B",
            roll = 1,
        }), HttpStatusCode.Created);

        await Seed(async conn =>
        {
            await MarkPresent(conn, tenantA, a1.GetProperty("id").GetGuid(), today, "present");
            await MarkPresent(conn, tenantA, a2.GetProperty("id").GetGuid(), today, "absent");
            await MarkPresent(conn, tenantB, b1.GetProperty("id").GetGuid(), today, "present");
        });

        var aggA = await AsTenant(app, tenantA, repo => repo.SchoolWideAsync(tenantA, today));

        aggA.Total.Should().Be(2);
        aggA.Present.Should().Be(1);
        aggA.Absent.Should().Be(1);
        aggA.Pct.Should().Be(50.00m);

        var aggB = await AsTenant(app, tenantB, repo => repo.SchoolWideAsync(tenantB, today));

        aggB.Total.Should().Be(1);
        aggB.Present.Should().Be(1);
        aggB.Absent.Should().Be(0);
        aggB.Pct.Should().Be(100.00m);
    }
}
