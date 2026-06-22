using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class PasswordResetTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    private async Task<string> InsertUserAsync(SqlConnectionFactory factory, string email)
    {
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync("INSERT dbo.Users (Id, Email, IsPlatform) VALUES (NEWID(),@e,0)",
            new { e = email });
        return email;
    }

    private static SqlConnectionFactory Factory(SqlServerFixture fx)
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        return new SqlConnectionFactory(fx.ConnectionString, ctx);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public async Task Forgot_returns_404_for_unregistered_and_200_for_registered()
    {
        var factory = Factory(fx);
        var email = await InsertUserAsync(factory, $"reset{Guid.NewGuid():N}@x.com");

        await using var app = App();
        var client = app.CreateClient();

        var unknown = await client.PostAsJsonAsync("/v1/auth/password/forgot",
            new { identifier = "nobody-reset@x.com" });
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var err = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
            err.RootElement.GetProperty("error").GetProperty("message").GetString()
                .Should().Be("Email is not registered.");

        (await client.PostAsJsonAsync("/v1/auth/password/forgot", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var c = await factory.OpenAsync();
        var count = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.OtpCodes WHERE Identifier = @id", new { id = email });
        count.Should().BeGreaterThan(0);
    }
}
