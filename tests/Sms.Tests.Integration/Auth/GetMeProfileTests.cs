using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class GetMeProfileTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> AppWithDb() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    [Fact]
    public async Task Teacher_me_returns_name_and_title_from_linked_Teachers_row()
    {
        var hasher = new Sms.Shared.Kernel.Auth.PasswordHasher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Email, PasswordHash, Name) VALUES (@userId, @tenantId, @email, @hash, 'Jane Teacher')",
                new { userId, tenantId, email = $"t{Guid.NewGuid():N}@x.com", hash = hasher.Hash("Pass123!") });
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (TenantId, Name, Designation, UserId) VALUES (@tenantId, 'Jane Teacher', 'Senior Teacher', @userId)",
                new { tenantId, userId });
            await conn.ExecuteAsync(
                "INSERT dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.teacher')", new { userId });
        }

        await using var app = AppWithDb();
        var jwt = new Sms.Shared.Kernel.Auth.JwtTokenService(
            new Sms.Shared.Kernel.Auth.JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = "integration-test-signing-key-32-bytes-min!!", AccessTokenMinutes = 15 },
            new Sms.Shared.Kernel.Time.SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { "school.teacher" }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Jane Teacher");
        data.GetProperty("title").GetString().Should().Be("Senior Teacher");
    }

    [Fact]
    public async Task Principal_me_returns_name_but_null_title()
    {
        var hasher = new Sms.Shared.Kernel.Auth.PasswordHasher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Email, PasswordHash, Name) VALUES (@userId, @tenantId, @email, @hash, 'Priya Principal')",
                new { userId, tenantId, email = $"p{Guid.NewGuid():N}@x.com", hash = hasher.Hash("Pass123!") });
        }

        await using var app = AppWithDb();
        var jwt = new Sms.Shared.Kernel.Auth.JwtTokenService(
            new Sms.Shared.Kernel.Auth.JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = "integration-test-signing-key-32-bytes-min!!", AccessTokenMinutes = 15 },
            new Sms.Shared.Kernel.Time.SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { "school.principal" }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Priya Principal");
        data.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
