using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Staffing;

[Collection("sql")]
public class StaffingPhotoTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient TenantClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["admin"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task<Guid> SeedLinkedTeacherAsync(SqlServerFixture fx, Guid tenantId)
    {
        var ctx = new TenantContext(); ctx.Set(tenantId, Guid.NewGuid(), false);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var userId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Email, IsPlatform) VALUES (@userId, @tenantId, @email, 0)",
            new { userId, tenantId, email = $"teacher{Guid.NewGuid():N}@x.com" });
        await c.ExecuteAsync(
            "INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES (@teacherId, @tenantId, 'Linked Teacher', @userId)",
            new { teacherId, tenantId, userId });
        return teacherId;
    }

    private static async Task<string?> GetUserPhotoAsync(SqlServerFixture fx, Guid tenantId, Guid teacherId)
    {
        var ctx = new TenantContext(); ctx.Set(tenantId, Guid.NewGuid(), false);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        await using var c = await factory.OpenAsync();
        return await c.QuerySingleAsync<string?>(
            "SELECT u.PhotoUrl FROM dbo.Users u JOIN dbo.Teachers t ON t.UserId = u.Id WHERE t.Id = @teacherId",
            new { teacherId });
    }

    [Fact]
    public async Task Setting_a_linked_teachers_photo_writes_through_to_their_Users_row()
    {
        var tenantId = Guid.NewGuid();
        var teacherId = await SeedLinkedTeacherAsync(fx, tenantId);

        await using var app = App();
        var client = TenantClient(app, tenantId);

        var updated = await Data(await client.PatchAsJsonAsync($"/v1/teachers/{teacherId}",
            new { photo_url = "https://cdn.example.com/teachers/a.png", set_photo = true }), HttpStatusCode.OK);
        updated.GetProperty("name").GetString().Should().Be("Linked Teacher");

        (await GetUserPhotoAsync(fx, tenantId, teacherId)).Should().Be("https://cdn.example.com/teachers/a.png");
    }

    [Fact]
    public async Task Setting_a_photo_on_an_unlinked_teacher_returns_409()
    {
        var tenantId = Guid.NewGuid();
        await using var app = App();
        var client = TenantClient(app, tenantId);

        var created = await Data(await client.PostAsJsonAsync("/v1/teachers", new { name = "Unlinked Teacher" }),
            HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        var res = await client.PatchAsJsonAsync($"/v1/teachers/{id}",
            new { photo_url = "https://cdn.example.com/a.png", set_photo = true });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rejects_a_teacher_photo_value_that_is_not_a_data_uri_or_http_url()
    {
        var tenantId = Guid.NewGuid();
        var teacherId = await SeedLinkedTeacherAsync(fx, tenantId);

        await using var app = App();
        var client = TenantClient(app, tenantId);

        var res = await client.PatchAsJsonAsync($"/v1/teachers/{teacherId}",
            new { photo_url = "not-a-valid-value", set_photo = true });
        res.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Updating_other_teacher_fields_without_set_photo_leaves_the_photo_untouched()
    {
        var tenantId = Guid.NewGuid();
        var teacherId = await SeedLinkedTeacherAsync(fx, tenantId);

        await using var app = App();
        var client = TenantClient(app, tenantId);

        await client.PatchAsJsonAsync($"/v1/teachers/{teacherId}",
            new { photo_url = "https://cdn.example.com/a.png", set_photo = true });

        var res = await client.PatchAsJsonAsync($"/v1/teachers/{teacherId}", new { status = "inactive" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserPhotoAsync(fx, tenantId, teacherId)).Should().Be("https://cdn.example.com/a.png");
    }
}
