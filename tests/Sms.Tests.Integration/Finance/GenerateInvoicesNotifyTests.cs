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
using Sms.Shared.Kernel.Results;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class GenerateInvoicesNotifyTests(SqlServerFixture fx)
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

    private static async Task<(Guid tenantId, Guid principalUserId, Guid studentId)> SeedAsync(
        SqlServerFixture fx, string guardianEmail, string guardianPhone, Guid? guardianUserId)
    {
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

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
        if (guardianUserId is { } gid)
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name, Email) VALUES (@gid, @tenantId, 'Guardian Of Aarav', @guardianEmail)",
                new { gid, tenantId, guardianEmail });
        }

        return (tenantId, principalUserId, studentId);
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId, string role)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { role }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task UpsertGradeFiveStructureAsync(HttpClient client)
    {
        var upsert = await client.PutAsJsonAsync("/v1/fees/structure", new
        {
            name = "Standard",
            academic_year = "2026",
            currency = "INR",
            effective_from = "2026-01-01",
            status = "active",
            amounts = new Dictionary<string, object> { ["5"] = new Dictionary<string, object> { ["Tuition"] = 8500 } },
        });
        upsert.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GenerateInvoices_fires_one_notification_per_created_invoice_with_the_real_amount_and_guardian_contacts()
    {
        var fake = new CapturingAnnouncementService();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services => services.AddScoped<IAnnouncementService>(_ => fake));
        });

        var guardianUserId = Guid.NewGuid();
        var (tenantId, principalUserId, studentId) = await SeedAsync(
            fx, "guardian-aarav@school.test", "+91-9000000000", guardianUserId);
        var client = AuthedClient(app, tenantId, principalUserId, Policies.Principal);

        await UpsertGradeFiveStructureAsync(client);

        var generate = await client.PostAsJsonAsync("/v1/fees/invoices/generate", new
        {
            academic_year = "2026",
            term = "Term 2",
            grades = new[] { "5" },
            due_date = "2026-03-15",
        });
        generate.StatusCode.Should().Be(HttpStatusCode.OK);

        fake.Created.Should().ContainSingle();
        var notify = fake.Created[0];
        notify.Audience.Should().Be("specific");
        notify.Emails.Should().ContainSingle().Which.Should().Be("guardian-aarav@school.test");
        notify.Phones.Should().ContainSingle().Which.Should().Be("+91-9000000000");
        notify.Channels.Should().BeEquivalentTo(new[] { "email", "app" });
        notify.UserId.Should().Be(guardianUserId);
        notify.Body.Should().Contain("8,500").And.Contain("2026 Term 2");
    }

    [Fact]
    public async Task GenerateInvoices_still_creates_the_invoice_even_if_notifying_the_guardian_throws()
    {
        var fake = new CapturingAnnouncementService { ThrowOnCreate = true };
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services => services.AddScoped<IAnnouncementService>(_ => fake));
        });

        var (tenantId, principalUserId, studentId) = await SeedAsync(
            fx, "guardian-aarav@school.test", "+91-9000000000", null);
        var client = AuthedClient(app, tenantId, principalUserId, Policies.Principal);

        await UpsertGradeFiveStructureAsync(client);

        var generate = await client.PostAsJsonAsync("/v1/fees/invoices/generate", new
        {
            academic_year = "2026",
            term = "Term 2",
            grades = new[] { "5" },
            due_date = "2026-03-15",
        });
        generate.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await generate.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("created").GetInt32().Should().Be(1);
        fake.CallCount.Should().Be(1, "the notify attempt must actually happen, not be skipped, before failing best-effort");

        var invoices = await client.GetAsync($"/v1/fees/invoices?student_id={studentId}");
        invoices.StatusCode.Should().Be(HttpStatusCode.OK);
        using var invDoc = System.Text.Json.JsonDocument.Parse(await invoices.Content.ReadAsStringAsync());
        invDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);
    }
}
