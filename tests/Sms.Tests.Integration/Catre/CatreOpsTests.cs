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

namespace Sms.Tests.Integration.Catre;

[Collection("sql")]
public class CatreOpsTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient PlatformClient(WebApplicationFactory<Program> app)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), null, ["owner"], isPlatform: true);
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

    [Fact]
    public async Task Onboarding_create_advance_and_toggle_checklist()
    {
        await using var app = App();
        var client = PlatformClient(app);

        var created = await Data(await client.PostAsJsonAsync("/v1/onboarding", new
        {
            name = "Lead School", slug = $"lead-{Guid.NewGuid():N}", owner = "Karthik", value = 4999
        }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("stage").GetString().Should().Be("lead");
        created.GetProperty("checklist").GetArrayLength().Should().Be(5);
        created.GetProperty("done").GetInt32().Should().Be(0);

        var advanced = await Data(
            await client.PostAsJsonAsync($"/v1/onboarding/{id}/advance", new { stage = "trial" }), HttpStatusCode.OK);
        advanced.GetProperty("stage").GetString().Should().Be("trial");

        var patched = await Data(await client.PatchAsJsonAsync($"/v1/onboarding/{id}/checklist",
            new { label = "Admin invited", done = true }), HttpStatusCode.OK);
        patched.GetProperty("done").GetInt32().Should().Be(1);

        var list = await Data(await client.GetAsync("/v1/onboarding"), HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Ticket_create_add_message_and_update()
    {
        await using var app = App();
        var client = PlatformClient(app);

        var created = await Data(await client.PostAsJsonAsync("/v1/tickets", new
        {
            subject = "Cannot import students", priority = "high"
        }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("messages").GetArrayLength().Should().Be(0);

        var withMsg = await Data(await client.PostAsJsonAsync($"/v1/tickets/{id}/messages",
            new { text = "We are looking into it." }), HttpStatusCode.Created);
        withMsg.GetProperty("messages_count").GetInt32().Should().Be(1);
        withMsg.GetProperty("messages").GetArrayLength().Should().Be(1);
        withMsg.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("agent");

        var updated = await Data(await client.PatchAsJsonAsync($"/v1/tickets/{id}",
            new { status = "resolved", assignee = "Rohan" }), HttpStatusCode.OK);
        updated.GetProperty("status").GetString().Should().Be("resolved");
        updated.GetProperty("assignee").GetString().Should().Be("Rohan");
    }

    [Fact]
    public async Task Team_invite_list_and_update()
    {
        await using var app = App();
        var client = PlatformClient(app);

        var invited = await Data(await client.PostAsJsonAsync("/v1/team", new
        {
            name = "Asha", email = $"asha{Guid.NewGuid():N}@catre.io", role = "support"
        }), HttpStatusCode.Created);
        var id = invited.GetProperty("id").GetGuid();
        invited.GetProperty("status").GetString().Should().Be("invited");

        var list = await Data(await client.GetAsync("/v1/team"), HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);

        var updated = await Data(await client.PatchAsJsonAsync($"/v1/team/{id}",
            new { status = "active" }), HttpStatusCode.OK);
        updated.GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task Audit_list_returns_seeded_entry()
    {
        var ctx = new TenantContext();
        ctx.Set(null, Guid.NewGuid(), isPlatform: true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var action = "suspend client " + Guid.NewGuid().ToString("N");
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.AuditLog (Id, ActorId, ActorName, Role, Action, Target, Kind) " +
                "VALUES (NEWID(), @a, 'Rohan', 'admin', @act, 'Greenwood', 'suspend')",
                new { a = Guid.NewGuid(), act = action });

        await using var app = App();
        var client = PlatformClient(app);
        var list = await Data(await client.GetAsync("/v1/audit"), HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain(action);
    }

    [Fact]
    public async Task Reports_revenue_and_csv()
    {
        await using var app = App();
        var client = PlatformClient(app);

        var planId = (await Data(await client.PostAsJsonAsync("/v1/plans", new
        {
            name = "Gold", tier = "gold", pricing = "flat", price = 14999, period = "month",
            limits = new { students = 1000, staff = 100, storage_gb = 40 }, visibility = "published", audience = "all"
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var clientId = (await Data(await client.PostAsJsonAsync("/v1/clients", new
        {
            name = "Greenwood", slug = $"g-{Guid.NewGuid():N}", plan_id = planId, trial_days = 14
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/v1/clients/{clientId}/status", new { status = "active" });

        var rev = await Data(await client.GetAsync("/v1/reports/revenue"), HttpStatusCode.OK);
        rev.GetProperty("arr").GetDecimal().Should().BeGreaterThanOrEqualTo(14999 * 12);
        rev.GetProperty("plan_performance").GetArrayLength().Should().BeGreaterThan(0);

        var csv = await client.GetAsync("/v1/reports/clients.csv");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync()).Should().StartWith("client,status,plan,mrr");
    }
}
