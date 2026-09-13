using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Sms.Tests.Integration;

namespace Sms.Tests.Integration.Finance;

/// Covers M0193_FeeStructure_History: every Save creates a new, immutable FeeStructures row
/// instead of overwriting the previous one, GET /fees/structure keeps resolving "the current
/// one" to edit as the most recent row, and GET /fees/structures lists every saved version.
[Collection("sql")]
public class FeeStructureHistoryTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient PrincipalClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(expected, because: body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> SaveStructureAsync(HttpClient client, string name, string academicYear) =>
        await Data(await client.PutAsJsonAsync("/v1/fees/structure", new
        {
            name,
            academic_year = academicYear,
            currency = "INR",
            effective_from = "2025-04-01",
            status = "active",
            amounts_json = """{"X-A":{"tuition":1000}}""",
        }), HttpStatusCode.OK);

    [Fact]
    public async Task Saving_twice_creates_two_history_entries_instead_of_overwriting()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = PrincipalClient(app, tenantId);

        var first = await SaveStructureAsync(client, "AY fees v1", "2025-26");
        var second = await SaveStructureAsync(client, "AY fees v2", "2025-26");

        first.GetProperty("id").GetGuid().Should().NotBe(second.GetProperty("id").GetGuid(),
            "each Save must create a new row, not update the previous one");

        var history = await Data(await client.GetAsync("/v1/fees/structures"), HttpStatusCode.OK);
        history.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Get_structure_still_resolves_to_the_most_recently_saved_version()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = PrincipalClient(app, tenantId);

        await SaveStructureAsync(client, "Older version", "2025-26");
        var latest = await SaveStructureAsync(client, "Newest version", "2025-26");

        var current = await Data(await client.GetAsync("/v1/fees/structure"), HttpStatusCode.OK);
        current.GetProperty("id").GetGuid().Should().Be(latest.GetProperty("id").GetGuid());
        current.GetProperty("name").GetString().Should().Be("Newest version");
    }

    [Fact]
    public async Task History_list_returns_every_saved_version_newest_first_without_amounts()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = PrincipalClient(app, tenantId);

        await SaveStructureAsync(client, "Version A", "2024-25");
        await SaveStructureAsync(client, "Version B", "2025-26");
        await SaveStructureAsync(client, "Version C", "2026-27");

        var history = await Data(await client.GetAsync("/v1/fees/structures"), HttpStatusCode.OK);
        var names = history.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        names.Should().Equal(["Version C", "Version B", "Version A"], "newest saved version first");

        foreach (var entry in history.EnumerateArray())
        {
            entry.TryGetProperty("amounts", out _).Should().BeFalse("the history list must stay light — no amounts payload");
            entry.TryGetProperty("created_at", out var createdAt).Should().BeTrue();
            createdAt.ValueKind.Should().NotBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task A_past_version_can_be_fetched_by_id_with_its_full_amounts()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = PrincipalClient(app, tenantId);

        var older = await SaveStructureAsync(client, "Old version", "2025-26");
        await SaveStructureAsync(client, "New version", "2025-26");

        var oldId = older.GetProperty("id").GetGuid();
        var fetched = await Data(await client.GetAsync($"/v1/fees/structures/{oldId}"), HttpStatusCode.OK);
        fetched.GetProperty("id").GetGuid().Should().Be(oldId);
        fetched.GetProperty("name").GetString().Should().Be("Old version");
        fetched.GetProperty("amounts").GetProperty("X-A").GetProperty("tuition").GetDecimal().Should().Be(1000);
    }
}
