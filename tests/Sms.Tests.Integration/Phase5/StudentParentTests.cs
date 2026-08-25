using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Phase5;

[Collection("sql")]
public class StudentParentTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient TenantClient(WebApplicationFactory<Program> app, Guid tenantId, string role)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, [role], isPlatform: false);
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
    public async Task Student_homework_status_and_submit()
    {
        await using var app = App();
        var client = TenantClient(app, Guid.NewGuid(), "student");
        var studentId = Guid.NewGuid();

        var created = await Data(await client.PostAsJsonAsync("/v1/homework", new
        {
            student_id = studentId, title = "Problem Set 14 — Quadratics", due_date = "2026-07-10",
            due_time = "11:59 PM", priority = "high"
        }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be("todo");

        var progress = await Data(await client.PatchAsJsonAsync($"/v1/homework/{id}",
            new { status = "progress" }), HttpStatusCode.OK);
        progress.GetProperty("status").GetString().Should().Be("progress");

        var submitted = await Data(await client.PostAsync($"/v1/homework/{id}/submit", null), HttpStatusCode.OK);
        submitted.GetProperty("status").GetString().Should().Be("submitted");

        var list = await Data(await client.GetAsync($"/v1/homework?student_id={studentId}"), HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Parent_pays_fee_invoice_via_gateway()
    {
        await using var app = App();
        var client = TenantClient(app, Guid.NewGuid(), "parent");
        var childId = Guid.NewGuid();

        var inv = await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = childId, period = "Term 4 · 2026", due_date = "2026-05-05", amount = 1240
        }), HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetGuid();
        inv.GetProperty("status").GetString().Should().Be("due");

        var paid = await Data(await client.PostAsync($"/v1/fees/invoices/{id}/pay", null), HttpStatusCode.OK);
        paid.GetProperty("amount").GetDecimal().Should().Be(1240);
        paid.GetProperty("method").GetString().Should().NotBeNullOrEmpty();

        // paying again -> conflict
        (await client.PostAsync($"/v1/fees/invoices/{id}/pay", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
