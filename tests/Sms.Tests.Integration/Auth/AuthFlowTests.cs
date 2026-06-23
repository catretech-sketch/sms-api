using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class AuthFlowTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> AppWithDb() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    [Fact]
    public async Task Login_with_seeded_user_returns_tokens_and_me_works()
    {
        var hasher = new PasswordHasher();
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var email = $"admin{Guid.NewGuid():N}@x.com";
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.Users (Id, Email, PasswordHash, IsPlatform) VALUES (NEWID(),@e,@h,1)",
                new { e = email, h = hasher.Hash("Pass123!") });

        await using var app = AppWithDb();
        var client = app.CreateClient();

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password = "Pass123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await login.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var accessToken = doc.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var me = await client.GetAsync("/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_with_mobile_number_returns_tokens()
    {
        var hasher = new PasswordHasher();
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var phone = $"+9198{Guid.NewGuid():N}".Substring(0, 13);
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.Users (Id, Phone, PasswordHash, IsPlatform) VALUES (NEWID(),@p,@h,1)",
                new { p = phone, h = hasher.Hash("Pass123!") });

        await using var app = AppWithDb();
        var client = app.CreateClient();

        // No '@' → backend looks the user up by phone instead of email.
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { phone, password = "Pass123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await login.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data").GetProperty("access_token").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Me_exposes_is_platform_for_a_platform_user()
    {
        var hasher = new PasswordHasher();
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var email = $"plat{Guid.NewGuid():N}@x.com";
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.Users (Id, Email, PasswordHash, IsPlatform) VALUES (NEWID(),@e,@h,1)",
                new { e = email, h = hasher.Hash("Pass123!") });

        await using var app = AppWithDb();
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password = "Pass123!" });
        var token = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var me = await client.GetAsync("/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("is_platform").GetBoolean().Should().BeTrue();
    }
}
