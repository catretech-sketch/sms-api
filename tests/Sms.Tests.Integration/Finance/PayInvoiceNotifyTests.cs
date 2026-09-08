using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Common;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class PayInvoiceNotifyTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private sealed class CapturingAnnouncementService : IAnnouncementService
    {
        public List<CreateAnnouncementRequest> Created { get; } = [];
        public bool ThrowOnCreate { get; set; }
        public int CallCount { get; private set; }

        public Task<ApiResult<IReadOnlyList<AnnouncementResponse>>> ListAsync(string? audience, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<AnnouncementResponse>>.Ok(Array.Empty<AnnouncementResponse>()));

        public Task<ApiResult<AnnouncementResponse>> CreateAsync(
            CreateAnnouncementRequest req, Guid? creatorUserId, string? role, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowOnCreate)
                throw new InvalidOperationException("simulated announcement failure");
            Created.Add(req);
            return Task.FromResult(ApiResult<AnnouncementResponse>.Ok(
                new AnnouncementResponse(Guid.NewGuid(), Guid.Empty, req.Title, req.Body, DateTime.UtcNow, null, role, req.Type ?? "general", false, req.Audience)));
        }
    }

    private static WebApplicationFactory<Program> BuildApp(SqlServerFixture fx, CapturingAnnouncementService fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services => services.AddScoped<IAnnouncementService>(_ => fake));
        });

    private static async Task<(Guid tenantId, Guid principalUserId, Guid studentId, Guid invoiceId)> SeedInvoiceAsync(
        SqlServerFixture fx, string guardianEmail, string guardianPhone, Guid? guardianUserId = null)
    {
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
            new { principalUserId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade, GuardianEmail, GuardianPhone) " +
            "VALUES (@studentId, @tenantId, 'A100', 'Aarav Sharma', 'active', '5', @guardianEmail, @guardianPhone)",
            new { studentId, tenantId, guardianEmail, guardianPhone });
        await conn.ExecuteAsync(
            "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, DueDate, Amount, Status) " +
            "VALUES (@invoiceId, @tenantId, @studentId, 'Term 2 2026', '2026-03-15', 8500, 'due')",
            new { invoiceId, tenantId, studentId });
        if (guardianUserId is { } gid)
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name, Email) VALUES (@gid, @tenantId, 'Guardian Of Aarav', @guardianEmail)",
                new { gid, tenantId, guardianEmail });
        }

        return (tenantId, principalUserId, studentId, invoiceId);
    }

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { Policies.Principal }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Cash_payment_fires_one_notification_with_the_real_amount_and_guardian_contacts()
    {
        var fake = new CapturingAnnouncementService();
        var app = BuildApp(fx, fake);
        var guardianUserId = Guid.NewGuid();
        var (tenantId, principalUserId, _, invoiceId) = await SeedInvoiceAsync(
            fx, "guardian-aarav@school.test", "+91-9000000000", guardianUserId);
        var client = Client(app, tenantId, principalUserId);

        var pay = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new { amount = 8500, method = "Cash" });
        pay.StatusCode.Should().Be(HttpStatusCode.OK);

        fake.Created.Should().ContainSingle();
        var notify = fake.Created[0];
        notify.Title.Should().Be("Invoice Paid");
        notify.Audience.Should().Be("specific");
        notify.Emails.Should().ContainSingle().Which.Should().Be("guardian-aarav@school.test");
        notify.Phones.Should().ContainSingle().Which.Should().Be("+91-9000000000");
        notify.Channels.Should().BeEquivalentTo(new[] { "email", "app" });
        notify.UserId.Should().Be(guardianUserId);
        notify.Body.Should().Contain("8,500").And.Contain("Cash").And.Contain("Aarav Sharma");
    }

    [Fact]
    public async Task Idempotent_replay_does_not_notify_again()
    {
        var fake = new CapturingAnnouncementService();
        var app = BuildApp(fx, fake);
        var (tenantId, principalUserId, _, invoiceId) = await SeedInvoiceAsync(fx, "guardian-aarav@school.test", "+91-9000000000");
        var client = Client(app, tenantId, principalUserId);
        var idempotencyKey = Guid.NewGuid();

        var first = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay",
            new { amount = 8500, method = "Cash", idempotency_key = idempotencyKey });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.Created.Should().ContainSingle();

        var replay = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay",
            new { amount = 8500, method = "Cash", idempotency_key = idempotencyKey });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        fake.Created.Should().ContainSingle("a replayed idempotency key must not fire a second notification");
    }

    [Fact]
    public async Task Already_fully_paid_invoice_does_not_notify()
    {
        var fake = new CapturingAnnouncementService();
        var app = BuildApp(fx, fake);
        var (tenantId, principalUserId, _, invoiceId) = await SeedInvoiceAsync(fx, "guardian-aarav@school.test", "+91-9000000000");
        var client = Client(app, tenantId, principalUserId);

        (await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new { amount = 8500, method = "Cash" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        fake.Created.Should().ContainSingle();

        var secondAttempt = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new { amount = 8500, method = "Cash" });
        secondAttempt.StatusCode.Should().Be(HttpStatusCode.Conflict);

        fake.Created.Should().ContainSingle("an already-fully-paid invoice's rejected second payment must not notify");
    }

    [Fact]
    public async Task Notify_service_throwing_still_leaves_the_payment_recorded_and_the_api_call_successful()
    {
        var fake = new CapturingAnnouncementService { ThrowOnCreate = true };
        var app = BuildApp(fx, fake);
        var (tenantId, principalUserId, _, invoiceId) = await SeedInvoiceAsync(fx, "guardian-aarav@school.test", "+91-9000000000");
        var client = Client(app, tenantId, principalUserId);

        var pay = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new { amount = 8500, method = "Cash" });

        pay.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.CallCount.Should().Be(1, "the notify attempt must actually happen, not be skipped, before failing best-effort");

        var invoices = await client.GetAsync($"/v1/fees/invoices?student_id={await StudentIdForAsync(fx, tenantId)}");
        invoices.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> StudentIdForAsync(SqlServerFixture fx, Guid tenantId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        return await conn.QuerySingleAsync<Guid>(
            "SELECT TOP 1 Id FROM dbo.Students WHERE TenantId = @tenantId", new { tenantId });
    }

    [Fact]
    public async Task Paying_student_A_invoice_never_notifies_student_B_guardian()
    {
        var fake = new CapturingAnnouncementService();
        var app = BuildApp(fx, fake);
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentAId = Guid.NewGuid();
        var studentBId = Guid.NewGuid();
        var invoiceAId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
                new { principalUserId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade, GuardianEmail, GuardianPhone) VALUES " +
                "(@studentAId, @tenantId, 'A100', 'Aarav Sharma', 'active', '5', 'guardian-a@school.test', '+91-9000000001'), " +
                "(@studentBId, @tenantId, 'B100', 'Bela Iyer', 'active', '5', 'guardian-b@school.test', '+91-9000000002')",
                new { studentAId, studentBId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, DueDate, Amount, Status) " +
                "VALUES (@invoiceAId, @tenantId, @studentAId, 'Term 2 2026', '2026-03-15', 8500, 'due')",
                new { invoiceAId, tenantId, studentAId });
        }

        var client = Client(app, tenantId, principalUserId);
        var pay = await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceAId}/pay", new { amount = 8500, method = "Cash" });
        pay.StatusCode.Should().Be(HttpStatusCode.OK);

        fake.Created.Should().ContainSingle();
        fake.Created[0].Emails.Should().ContainSingle().Which.Should().Be("guardian-a@school.test");
    }
}
