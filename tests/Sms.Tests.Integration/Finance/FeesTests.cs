using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
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
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(expected, because: body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Fee_invoice_generate_from_structure()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-FEE-1",
            name = "Fee Kid",
            grade = "X",
            section = "A",
            roll = 2,
        }), HttpStatusCode.Created);
        student.GetProperty("id").GetGuid().Should().NotBeEmpty();

        await Data(await client.PutAsJsonAsync("/v1/fees/structure", new
        {
            name = "AY fees",
            academic_year = "2025-26",
            currency = "INR",
            effective_from = "2025-04-01",
            status = "active",
            amounts_json = """{"X-A":{"tuition":1000},"X":{"tuition":1000}}""",
        }), HttpStatusCode.OK);

        var gen = await Data(await client.PostAsJsonAsync("/v1/fees/invoices/generate", new
        {
            academic_year = "2025-26",
            term = "Term 1",
            classes = new[] { "X-A" },
        }), HttpStatusCode.OK);
        gen.GetProperty("created").GetInt32().Should().BeGreaterThan(0);

        var list = await Data(await client.GetAsync("/v1/fees/invoices"), HttpStatusCode.OK);
        list.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Fee_report_summary_returns_kpis_from_invoices_and_payments()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-SUM-1",
            name = "Summary Kid",
            grade = "X",
            section = "B",
            roll = 3,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();

        await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = studentId,
            period = "2025-26 Term 1",
            due_date = "2026-07-01",
            amount = 10000,
        }), HttpStatusCode.Created);

        await Data(await client.PostAsJsonAsync("/v1/fees/payments", new
        {
            student_id = studentId,
            student_name = "Summary Kid",
            class_label = "X-B",
            fee_type = "academic",
            amount = 4000,
            method = "UPI",
            @ref = "SUM-UPI-1",
        }), HttpStatusCode.Created);

        var summary = await Data(await client.GetAsync("/v1/fees/reports/summary"), HttpStatusCode.OK);
        summary.GetProperty("billed_term").GetDecimal().Should().Be(10000);
        summary.GetProperty("outstanding").GetDecimal().Should().Be(10000);
        summary.GetProperty("collected_term").GetDecimal().Should().Be(4000);
        summary.GetProperty("defaulters").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        summary.GetProperty("by_mode").GetArrayLength().Should().BeGreaterThan(0);
        summary.TryGetProperty("latest_payment", out var latest).Should().BeTrue();
        latest.ValueKind.Should().NotBe(JsonValueKind.Null);
        latest.GetProperty("amount").GetDecimal().Should().Be(4000);
    }

    [Fact]
    public async Task Fee_partial_payment_accumulates_paid_amount()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-PART-1",
            name = "Partial Kid",
            grade = "I",
            section = "A",
            roll = 1,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();

        var inv = await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = studentId,
            period = "2025-26 Term 1",
            amount = 133055,
        }), HttpStatusCode.Created);
        var invoiceId = inv.GetProperty("id").GetGuid();

        await Data(await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new
        {
            amount = 5000,
            mode = "Cash",
            student_name = "Partial Kid",
            cls = "I-A",
            fee_type = "All fee types",
        }), HttpStatusCode.OK);

        var list = await Data(await client.GetAsync("/v1/fees/invoices"), HttpStatusCode.OK);
        var row = list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == invoiceId);
        row.GetProperty("status").GetString().Should().Be("partial");
        row.GetProperty("paid_amount").GetDecimal().Should().Be(5000);
        row.GetProperty("amount").GetDecimal().Should().Be(133055);
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

    [Fact]
    public async Task Fee_invoice_generate_skips_duplicate_student_period()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-FEE-DUP-1",
            name = "Dup Kid",
            grade = "X",
            section = "A",
            roll = 2,
        }), HttpStatusCode.Created);

        await Data(await client.PutAsJsonAsync("/v1/fees/structure", new
        {
            name = "AY fees",
            academic_year = "2025-26",
            currency = "INR",
            effective_from = "2025-04-01",
            status = "active",
            amounts_json = """{"X-A":{"tuition":1000},"X":{"tuition":1000}}""",
        }), HttpStatusCode.OK);

        var body = new
        {
            academic_year = "2025-26",
            term = "Term 1",
            classes = new[] { "X-A" },
        };
        var first = await Data(await client.PostAsJsonAsync("/v1/fees/invoices/generate", body), HttpStatusCode.OK);
        first.GetProperty("created").GetInt32().Should().BeGreaterThan(0);
        var second = await Data(await client.PostAsJsonAsync("/v1/fees/invoices/generate", body), HttpStatusCode.OK);
        second.GetProperty("created").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Fee_invoice_pay_persists_invoice_id_on_payment()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-PAY-LNK-1",
            name = "Link Kid",
            grade = "I",
            section = "A",
            roll = 1,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();

        var inv = await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = studentId,
            period = "2025-26 Term Link",
            amount = 5000,
        }), HttpStatusCode.Created);
        var invoiceId = inv.GetProperty("id").GetGuid();

        var paid = await Data(await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new
        {
            amount = 5000,
            mode = "UPI",
            cls = "I-A",
            head_id = "tuition",
            head_name = "Tuition",
        }), HttpStatusCode.OK);
        var paymentId = paid.GetProperty("id").GetGuid();
        paid.GetProperty("invoice_id").GetGuid().Should().Be(invoiceId);
        paid.GetProperty("head_id").GetString().Should().Be("tuition");
        paid.GetProperty("method").GetString().Should().Be("UPI");
        paid.GetProperty("class_label").GetString().Should().Be("I-A");
        paid.GetProperty("fee_type").GetString().Should().Be("Tuition");

        var listed = await Data(await client.GetAsync($"/v1/fees/payments?student_id={studentId}"), HttpStatusCode.OK);
        listed.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == paymentId)
            .GetProperty("invoice_id").GetGuid().Should().Be(invoiceId);

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenant", new { tenant });
        var row = await conn.QuerySingleAsync<(Guid InvoiceId, string HeadId, string Method)>(
            "SELECT InvoiceId, HeadId, Method FROM dbo.FeePayments WHERE Id = @paymentId",
            new { paymentId });
        row.InvoiceId.Should().Be(invoiceId);
        row.HeadId.Should().Be("tuition");
        row.Method.Should().Be("UPI");
    }
}
