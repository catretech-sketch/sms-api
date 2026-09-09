using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Payments;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class RazorpayFeeWebhookTests(SqlServerFixture fx)
{
    // >= 32 bytes, required by Sms.Shared.Kernel.Configuration.SecretsValidator at host startup —
    // this endpoint is [AllowAnonymous] but the host still validates Jwt:SigningKey is configured.
    private const string SigningKey = "integration-test-signing-key-32-bytes-min!!";

    private sealed class FakeRazorpayClient(bool signatureValid) : IRazorpayClient
    {
        public Task<RazorpayOrderCreated> CreateOrderAsync(
            string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct = default) =>
            Task.FromResult(new RazorpayOrderCreated("unused", amountPaise, currency));
        public bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature) => signatureValid;
        public bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader) => signatureValid;
    }

    private static WebApplicationFactory<Program> App(SqlServerFixture fx, bool signatureValid = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", SigningKey);
            b.ConfigureTestServices(services => services.AddSingleton<IRazorpayClient>(new FakeRazorpayClient(signatureValid)));
        });

    private static async Task<(Guid tenantId, Guid invoiceId, string orderId)> SeedOrderAsync(
        WebApplicationFactory<Program> app, SqlServerFixture fx)
    {
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var orderId = $"order_webhook_{Guid.NewGuid():N}";
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade) " +
            "VALUES (@studentId, @tenantId, 'A400', 'Webhook Kid', 'active', '8')", new { studentId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, Amount, PaidAmount, Status) " +
            "VALUES (@invoiceId, @tenantId, @studentId, 'Term 1', 2000, 0, 'due')", new { invoiceId, tenantId, studentId });
        // GetActiveAsync unconditionally Unprotect()s these — must be produced via the app's own
        // IDataProtectionProvider under the same purpose TenantPaymentCredentialService uses, or it throws.
        using var scope = app.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("TenantPaymentCredentials.Razorpay.v1");
        var keySecretEncrypted = protector.Protect("irrelevant-for-this-test-fake-client");
        var webhookSecretEncrypted = protector.Protect("irrelevant-webhook-secret-fake-client");
        await conn.ExecuteAsync(
            "INSERT dbo.TenantPaymentCredentials (TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled) " +
            "VALUES (@tenantId, 'razorpay', 'rzp_test_wh', @keySecretEncrypted, @webhookSecretEncrypted, 'test', 1)",
            new { tenantId, keySecretEncrypted, webhookSecretEncrypted });
        await conn.ExecuteAsync(
            "INSERT dbo.FeePaymentOrders (Id, TenantId, InvoiceId, RazorpayOrderId, AmountPaise, Status, InitiatedBy) " +
            "VALUES (NEWID(), @tenantId, @invoiceId, @orderId, 200000, 'Created', 'parent')", new { tenantId, invoiceId, orderId });
        return (tenantId, invoiceId, orderId);
    }

    private static StringContent WebhookBody(string orderId, string paymentId, string evt = "payment.captured") =>
        new(JsonSerializer.Serialize(new
        {
            @event = evt,
            payload = new { payment = new { entity = new { order_id = orderId, id = paymentId } } },
        }), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Payment_captured_webhook_records_payment_without_any_client_confirm()
    {
        await using var app = App(fx);
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(app, fx);
        var client = app.CreateClient();

        var res = await client.PostAsync("/v1/webhooks/razorpay-fees",
            WebhookBody(orderId, "pay_WEBHOOK_ONLY"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        var status = await conn.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.FeeInvoices WHERE Id = @invoiceId", new { invoiceId });
        status.Should().Be("paid");
    }

    [Fact]
    public async Task Webhook_after_client_verify_already_processed_it_does_not_duplicate()
    {
        await using var app = App(fx);
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(app, fx);
        var client = app.CreateClient();
        const string paymentId = "pay_RACE";

        var first = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, paymentId));
        var second = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, paymentId));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE InvoiceId = @invoiceId", new { invoiceId });
        paymentCount.Should().Be(1);
    }

    [Fact]
    public async Task Wrong_secret_webhook_signature_is_rejected_with_no_state_change()
    {
        await using var app = App(fx, signatureValid: false);
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(app, fx);
        var client = app.CreateClient();

        var res = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, "pay_BADSIG"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        var status = await conn.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.FeeInvoices WHERE Id = @invoiceId", new { invoiceId });
        status.Should().Be("due");
    }

    [Fact]
    public async Task Unknown_order_id_is_ignored_gracefully()
    {
        await using var app = App(fx);
        var client = app.CreateClient();
        var res = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody("order_does_not_exist", "pay_X"));
        res.StatusCode.Should().Be(HttpStatusCode.OK); // Razorpay should not retry forever on an order we'll never recognize
    }
}
