using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Time;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class InvitationLifecycleTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient AdminClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.admin"], isPlatform: false);
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }

    private async Task<Guid> SeedActiveTenant()
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var id = Guid.NewGuid();
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync("INSERT dbo.Tenants (Id, Name, Slug, Status, Tier) VALUES (@id,'T',@s,'active','gold')",
            new { id, s = $"t{id:N}" });
        return id;
    }

    private async Task<(Guid Id, string Status)> GetUserStatusAsync(string email)
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        await using var c = await factory.OpenAsync();
        return await c.QuerySingleAsync<(Guid, string)>(
            "SELECT Id, Status FROM dbo.Users WHERE Email = @email", new { email });
    }

    private async Task<(DateTime ExpiresAt, DateTime? AcceptedAt, string RoleLabel)> GetInvitationByEmailAsync(string email)
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        await using var c = await factory.OpenAsync();
        return await c.QuerySingleAsync<(DateTime, DateTime?, string)>(
            "SELECT ExpiresAt, AcceptedAt, RoleLabel FROM dbo.Invitations WHERE Email = @email", new { email });
    }

    private static string Sha256Hex(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

    [Fact]
    public async Task Invite_creates_pending_user_and_an_invitation_row_valid_for_24_hours()
    {
        var tid = await SeedActiveTenant();
        await using var app = App();
        var admin = AdminClient(app, tid);
        var email = $"teacher{Guid.NewGuid():N}@x.com";

        (await admin.PostAsJsonAsync("/v1/users",
            new { email, roles = new[] { "school.teacher" } })).StatusCode.Should().Be(HttpStatusCode.Created);

        var (_, status) = await GetUserStatusAsync(email);
        status.Should().Be("pending");

        var (expiresAt, acceptedAt, roleLabel) = await GetInvitationByEmailAsync(email);
        roleLabel.Should().Be("Teacher");
        acceptedAt.Should().BeNull();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Accepting_the_invite_via_password_reset_marks_it_accepted_and_activates_the_user()
    {
        var tid = await SeedActiveTenant();
        await using var app = App();
        var admin = AdminClient(app, tid);
        var email = $"teacher{Guid.NewGuid():N}@x.com";

        await admin.PostAsJsonAsync("/v1/users", new { email, roles = new[] { "school.teacher" } });

        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("UPDATE dbo.OtpCodes SET CodeHash=@h WHERE Identifier=@id",
                new { id = email, h = Sha256Hex("123456") });

        var anon = app.CreateClient();
        var reset = await anon.PostAsJsonAsync("/v1/auth/password/reset",
            new { identifier = email, code = "123456", password = "NewPassw0rd!" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (_, status) = await GetUserStatusAsync(email);
        status.Should().Be("active");

        var (_, acceptedAt, _) = await GetInvitationByEmailAsync(email);
        acceptedAt.Should().NotBeNull();
    }
}
