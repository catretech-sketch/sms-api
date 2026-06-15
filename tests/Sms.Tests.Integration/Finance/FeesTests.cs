using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class FeesTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient TenantClient(WebApplicationFactory<Program> app)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), Guid.NewGuid(), ["admin"], isPlatform: false);
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
    public async Task Fee_payment_create_and_list_by_student()
    {
        await using var app = App();
        var client = TenantClient(app);
        var studentId = Guid.NewGuid();

        var created = await Data(await client.PostAsJsonAsync("/v1/fees/payments", new
        {
            student_id = studentId, student_name = "Aarav", class_label = "X-A",
            fee_type = "academic", amount = 14999, method = "UPI", @ref = "UPI-8842019"
        }), HttpStatusCode.Created);
        created.GetProperty("fee_type").GetString().Should().Be("academic");
        created.GetProperty("amount").GetDecimal().Should().Be(14999);
        created.GetProperty("ref").GetString().Should().Be("UPI-8842019");

        var list = await Data(await client.GetAsync($"/v1/fees/payments?student_id={studentId}"), HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Should().Contain(created.GetProperty("id").GetGuid());
    }
}
