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

namespace Sms.Tests.Integration.Sis;

[Collection("sql")]
public class StudentAttendancePctLiveTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Student_attendance_pct_reflects_real_attendance_records()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var classId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status) VALUES (@studentId, @tenantId, 'A1', 'S1', 'active')",
                new { studentId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, StudentCount) VALUES (@classId, @tenantId, 'C1', 0)",
                new { classId, tenantId });
            // 3 present, 1 absent => 75%
            var statuses = new[] { "present", "present", "present", "absent" };
            for (int i = 0; i < statuses.Length; i++)
                await conn.ExecuteAsync(
                    "INSERT dbo.AttendanceRecords (TenantId, ClassId, StudentId, [Date], Status) VALUES (@tenantId, @classId, @studentId, @date, @status)",
                    new { tenantId, classId, studentId, date = DateTime.UtcNow.Date.AddDays(-i), status = statuses[i] });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync($"/v1/students/{studentId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("attendance_pct").GetDecimal().Should().Be(75.00m);
    }
}
