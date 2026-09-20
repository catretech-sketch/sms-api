# Razorpay Online Fee Payment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a parent (or school staff, on a parent's behalf) pay a fee invoice for real through Razorpay, settling into that school's own Razorpay account, reusing the existing payment-recording, idempotency, and notification machinery unchanged.

**Architecture:** New `FeeOnlinePaymentService` creates/verifies Razorpay orders per-tenant (own Key/Secret, encrypted at rest via ASP.NET Core Data Protection) and, on a verified payment, calls the **existing** `IFeeService.PayInvoiceAsync` exactly as the manual/cash path does — this is the key reuse point: `PayInvoiceAsync` already owns idempotency-key checking, amount validation, guardian notification (email+in-app, with PDF receipt), and live-broadcast, so a Razorpay payment is just another caller of that one method with `Method: "Razorpay"`. The existing `RazorpayGateway`'s HTTP/HMAC logic is extracted into a stateless `RazorpayClient` parameterized by credentials, so the existing Catre-billing caller (`PlanUpgradeService`) is unaffected while a new tenant-credentialed caller is added. A new webhook endpoint is an independent safety net using the same reuse point. Endpoint paths/shapes match `sms-admin`'s already-built (currently 404ing) frontend exactly, so no `sms-admin` API-layer changes are needed — only removing a pre-existing duplicate client-side notification call. `sms-student` gets a real Checkout flow via `react-native-webview` (no dependency exists yet) loading Razorpay's standard hosted Checkout page, avoiding any native module/prebuild requirement in this Expo-managed app.

**Tech Stack:** .NET 10, Dapper-backed repositories (raw SQL via `QueryInlineAsync`/`ExecuteInlineAsync`, no EF Core), `FluentMigrator`, ASP.NET Core Data Protection (`Microsoft.AspNetCore.DataProtection`), xUnit + `WebApplicationFactory<Program>` + real SQL Server (`SqlServerFixture`) integration tests. `sms-admin`: React + TanStack Query, Vitest. `sms-student`: Expo/React Native, `react-native-webview` (new dependency), Jest/RTL.

**Spec:** `docs/superpowers/specs/2026-09-09-razorpay-fee-payment-design.md`

## Global Constraints

- Never trust a client-supplied payment amount — always recompute `invoice.Amount - invoice.PaidAmount` server-side.
- Every new endpoint touching an invoice reuses the *exact* existing `FeeController.PayInvoice` authorization guard: `RoleChecks.IsStaff(User) || await sis.IsLinkedToCallerAsync(inv.StudentId, ct)`.
- A verified Razorpay payment must be recorded by calling the existing `IFeeService.PayInvoiceAsync(invoiceId, PayFeeInvoiceRequest, ct)` — never a new/duplicate call to `RecordInvoicePaymentAsync` or a second notification path. This is how idempotency and guardian notification are inherited for free.
- `IdempotencyKey` for a Razorpay payment is always the Razorpay `payment_id` (a `Guid` is required by `PayFeeInvoiceRequest.IdempotencyKey : Guid?` — see Task 4 for the deterministic-GUID derivation, since Razorpay payment ids are opaque strings like `pay_ABC123`, not GUIDs).
- Razorpay Key Secret / Webhook Secret are encrypted at rest via `IDataProtector`, decrypted only in-process, never logged, never returned by any GET endpoint.
- Razorpay credential configuration (`GET/PUT /school/integrations`, `POST /school/integrations/razorpay/verify`) is restricted to the `school.owner` role only (a new `Policies.SchoolOwner`-only authorization policy — the existing `Policies.SchoolAdmin`/`Policies.Principal` policies both also accept `SchoolOwner` but additionally accept broader roles, so neither can be reused here).
- Online payment is always for the full remaining balance — no partial-amount online payment.
- `FeatureCatalog.OnlineFeePayment` gates order-creation; `TenantPaymentCredentials.IsEnabled` + configured gates it independently. Both must pass.
- No refund endpoint, no partial online payments, no Razorpay Route, no Email/SMS integrations backend — all explicitly out of scope (see spec's Non-goals).

---

### Task 1: Migration — new tables, feature flag, Owner-only policy

**Files:**
- Create: `db/Sms.Migrations/M0180_Razorpay_Fee_Payment_Schema.cs` (next available number — confirm no other migration has landed on `phase-0-foundation` above `M0179` before starting; the uncommitted `M0180`-`M0183` files seen during Phase 1 belong to a different, unrelated in-progress branch and must NOT be reused or collided with — if they've since merged, renumber this migration accordingly)
- Modify: `src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs` (add `OnlineFeePayment` constant)
- Modify: `src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs` (add an Owner-only policy)
- Test: `tests/Sms.Tests.Integration/Finance/RazorpayFeePaymentSchemaTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `dbo.TenantPaymentCredentials` (TenantId PK, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled, CreatedAt, UpdatedAt), `dbo.FeePaymentOrders` (Id PK, TenantId, InvoiceId, RazorpayOrderId unique, AmountPaise, Status, InitiatedBy, CreatedAt, UpdatedAt); `FeatureCatalog.OnlineFeePayment = "fees.online_payment"`; policy name `Policies.OwnerOnly = "school.owner.only"` requiring only the `SchoolOwner` role.

- [ ] **Step 1: Write the failing schema test**

```csharp
using Microsoft.Data.SqlClient;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class RazorpayFeePaymentSchemaTests(SqlServerFixture fx)
{
    [Fact]
    public async Task TenantPaymentCredentials_and_FeePaymentOrders_tables_exist_with_expected_columns()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();

        var credColumns = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TenantPaymentCredentials'")).ToList();
        credColumns.Should().Contain(new[]
        {
            "TenantId", "Provider", "KeyId", "KeySecretEncrypted", "WebhookSecretEncrypted",
            "Mode", "IsEnabled", "CreatedAt", "UpdatedAt",
        });

        var orderColumns = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FeePaymentOrders'")).ToList();
        orderColumns.Should().Contain(new[]
        {
            "Id", "TenantId", "InvoiceId", "RazorpayOrderId", "AmountPaise", "Status",
            "InitiatedBy", "CreatedAt", "UpdatedAt",
        });

        var uniqueIndexes = (await conn.QueryAsync<string>(
            "SELECT i.name FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id " +
            "WHERE t.name = 'FeePaymentOrders' AND i.is_unique = 1")).ToList();
        uniqueIndexes.Should().Contain(n => n.Contains("RazorpayOrderId"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayFeePaymentSchemaTests"`
Expected: FAIL (tables don't exist yet — `INFORMATION_SCHEMA.COLUMNS` query returns no rows for either table)

- [ ] **Step 3: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(180, "Razorpay online fee payment: TenantPaymentCredentials + FeePaymentOrders")]
public sealed class M0180_Razorpay_Fee_Payment_Schema : Migration
{
    public override void Up()
    {
        Create.Table("TenantPaymentCredentials")
            .WithColumn("TenantId").AsGuid().PrimaryKey()
            .WithColumn("Provider").AsString(20).NotNullable().WithDefaultValue("razorpay")
            .WithColumn("KeyId").AsString(100).Nullable()
            .WithColumn("KeySecretEncrypted").AsCustom("nvarchar(max)").Nullable()
            .WithColumn("WebhookSecretEncrypted").AsCustom("nvarchar(max)").Nullable()
            .WithColumn("Mode").AsString(10).NotNullable().WithDefaultValue("test")
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        Create.Table("FeePaymentOrders")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("InvoiceId").AsGuid().NotNullable()
            .WithColumn("RazorpayOrderId").AsString(100).NotNullable()
            .WithColumn("AmountPaise").AsInt64().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("Created")
            .WithColumn("InitiatedBy").AsString(20).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        Create.Index("UX_FeePaymentOrders_RazorpayOrderId")
            .OnTable("FeePaymentOrders")
            .OnColumn("RazorpayOrderId").Ascending()
            .WithOptions().Unique();

        Create.Index("IX_FeePaymentOrders_Tenant_Invoice")
            .OnTable("FeePaymentOrders")
            .OnColumn("TenantId").Ascending()
            .OnColumn("InvoiceId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("FeePaymentOrders");
        Delete.Table("TenantPaymentCredentials");
    }
}
```

- [ ] **Step 4: Add the feature flag constant**

In `src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs`, under the `// Platinum` group (matches `AttendanceGeofence`'s tier — Owner-configured online payment collection is a premium capability):

```csharp
    public const string OnlineFeePayment = "fees.online_payment";
```

- [ ] **Step 5: Add the Owner-only authorization policy**

In `src/Sms.Shared.Kernel/Authz/Policies.cs`, add alongside the other policy-name constants:

```csharp
    public const string SchoolOwnerOnly = "school.owner.only"; // strictly Owner — narrower than SchoolAdmin/Principal, which also accept SchoolOwner
```

In `src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs`, add one line inside `AddSmsAuthorization`:

```csharp
            .AddPolicy(Policies.SchoolOwnerOnly, p => p.RequireRole(Policies.SchoolOwner))
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayFeePaymentSchemaTests"`
Expected: PASS

- [ ] **Step 7: Run the full test suite to check for regressions**

Run: `dotnet build && dotnet test`
Expected: all existing tests still pass; new schema test passes.

- [ ] **Step 8: Commit**

```bash
git add db/Sms.Migrations/M0180_Razorpay_Fee_Payment_Schema.cs src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs src/Sms.Shared.Kernel/Authz/Policies.cs tests/Sms.Tests.Integration/Finance/RazorpayFeePaymentSchemaTests.cs
git commit -m "feat(fees): add Razorpay fee-payment schema, feature flag, Owner-only policy"
```

---

### Task 2: Extract a tenant-parameterized `RazorpayClient` (zero change for Catre billing)

**Files:**
- Create: `src/Sms.Shared.Kernel/Payments/RazorpayClient.cs`
- Modify: `src/Sms.Shared.Kernel/Payments/RazorpayGateway.cs` (delegate to the new client instead of inlining HTTP/HMAC logic)
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs:112` (register `IRazorpayClient`)
- Test: `tests/Sms.Tests.Unit/Payments/RazorpayClientTests.cs`

**Interfaces:**
- Consumes: nothing new (pure extraction of existing `RazorpayGateway` logic).
- Produces: `IRazorpayClient` with `CreateOrderAsync(string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct)`, `bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature)`, `bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader)` — used directly by Task 4/5/6's new tenant-credentialed code, and internally by the unchanged `RazorpayGateway` (which still implements `IRazorpayGateway` for `PlanUpgradeService`).

- [ ] **Step 1: Write the failing unit test**

```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Payments;
using Xunit;

namespace Sms.Tests.Unit.Payments;

public class RazorpayClientTests
{
    private readonly RazorpayClient _client = new();

    [Fact]
    public void VerifyPaymentSignature_accepts_a_correctly_signed_payload()
    {
        const string secret = "test-secret";
        const string orderId = "order_ABC";
        const string paymentId = "pay_XYZ";
        var expected = ComputeHmac(secret, $"{orderId}|{paymentId}");

        _client.VerifyPaymentSignature(secret, orderId, paymentId, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyPaymentSignature_rejects_a_tampered_signature()
    {
        const string secret = "test-secret";
        _client.VerifyPaymentSignature(secret, "order_ABC", "pay_XYZ", "not-a-real-signature").Should().BeFalse();
    }

    [Fact]
    public void VerifyPaymentSignature_rejects_the_wrong_secret()
    {
        const string orderId = "order_ABC";
        const string paymentId = "pay_XYZ";
        var signedWithOtherSecret = ComputeHmac("some-other-secret", $"{orderId}|{paymentId}");

        _client.VerifyPaymentSignature("test-secret", orderId, paymentId, signedWithOtherSecret).Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_accepts_a_correctly_signed_body()
    {
        const string secret = "webhook-secret";
        const string body = """{"event":"payment.captured"}""";
        var expected = ComputeHmac(secret, body);

        _client.VerifyWebhookSignature(secret, body, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_rejects_a_tampered_body()
    {
        const string secret = "webhook-secret";
        var signature = ComputeHmac(secret, """{"event":"payment.captured"}""");

        _client.VerifyWebhookSignature(secret, """{"event":"payment.failed"}""", signature).Should().BeFalse();
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var key = System.Text.Encoding.UTF8.GetBytes(secret);
        var data = System.Text.Encoding.UTF8.GetBytes(payload);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayClientTests"`
Expected: FAIL with "The type or namespace name 'RazorpayClient' could not be found"

- [ ] **Step 3: Write `RazorpayClient`**

```csharp
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sms.Shared.Kernel.Payments;

public interface IRazorpayClient
{
    Task<RazorpayOrderCreated> CreateOrderAsync(
        string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct = default);
    bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature);
    bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader);
}

/// Stateless Razorpay HTTP/HMAC client — credentials are passed per-call so the same client
/// serves both the global platform account (Catre billing, via RazorpayGateway) and any number
/// of per-tenant accounts (fee payments, via FeeOnlinePaymentService), with zero duplicated logic.
public sealed class RazorpayClient(IHttpClientFactory httpClientFactory, ILogger<RazorpayClient> log) : IRazorpayClient
{
    public async Task<RazorpayOrderCreated> CreateOrderAsync(
        string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("razorpay");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        var payload = JsonSerializer.Serialize(new
        {
            amount = amountPaise,
            currency,
            receipt = receipt.Length > 40 ? receipt[..40] : receipt,
            payment_capture = 1,
        });
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Razorpay order create failed: {Status} {Body}", (int)res.StatusCode, body);
            throw new InvalidOperationException("Could not create Razorpay order.");
        }

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay order missing id.");
        var amount = doc.RootElement.GetProperty("amount").GetInt64();
        var curr = doc.RootElement.GetProperty("currency").GetString() ?? currency;
        return new RazorpayOrderCreated(id, amount, curr);
    }

    public bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId) || string.IsNullOrWhiteSpace(signature))
            return false;
        return HmacEquals($"{orderId}|{paymentId}", signature, keySecret);
    }

    public bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;
        return HmacEquals(body, signatureHeader, webhookSecret);
    }

    private static bool HmacEquals(string payload, string signatureHex, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        var actual = signatureHex.Trim().ToLowerInvariant();
        if (expected.Length != actual.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
```

Note: the unit test above constructs `new RazorpayClient()` with no arguments — since the real constructor requires `IHttpClientFactory`/`ILogger`, add a second, parameterless-friendly path by making `VerifyPaymentSignature`/`VerifyWebhookSignature`/`HmacEquals` `static` (they need no instance state) while `CreateOrderAsync` stays an instance method needing the HTTP client. Adjust the test file to call `RazorpayClient.VerifyPaymentSignature(...)` as a static call once written this way, and drop the `_client` field.

- [ ] **Step 4: Fix the test to call the static methods**

Update `RazorpayClientTests` to remove the `_client` field and call `RazorpayClient.VerifyPaymentSignature(...)` / `RazorpayClient.VerifyWebhookSignature(...)` directly (both are `static` per Step 3's note), and make the two verify methods `public static bool` in `RazorpayClient`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayClientTests"`
Expected: PASS

- [ ] **Step 6: Refactor `RazorpayGateway` to delegate, with zero behavior change**

Replace the body of `src/Sms.Shared.Kernel/Payments/RazorpayGateway.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Sms.Shared.Kernel.Payments;

public sealed class RazorpayGateway(IRazorpayClient client, IOptions<RazorpayOptions> options) : IRazorpayGateway
{
    private readonly RazorpayOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;
    public string KeyId => _opts.KeyId;

    public Task<RazorpayOrderCreated> CreateOrderAsync(
        long amountPaise, string currency, string receipt, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured)
            throw new InvalidOperationException("Razorpay is not configured.");
        return client.CreateOrderAsync(_opts.KeyId, _opts.KeySecret, amountPaise, currency, receipt, ct);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) =>
        RazorpayClient.VerifyPaymentSignature(_opts.KeySecret, orderId, paymentId, signature);

    public bool VerifyWebhookSignature(string body, string signatureHeader) =>
        RazorpayClient.VerifyWebhookSignature(_opts.WebhookSecret, body, signatureHeader);
}
```

- [ ] **Step 7: Register `IRazorpayClient` in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, right before the existing `builder.Services.AddSingleton<IRazorpayGateway, RazorpayGateway>();` line (around line 112):

```csharp
        builder.Services.AddSingleton<IRazorpayClient, RazorpayClient>();
```

- [ ] **Step 8: Run the full test suite to confirm zero regression in Catre billing**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~PlanUpgrade|FullyQualifiedName~Razorpay"`
Expected: PASS — every existing `PlanUpgradeService`/Razorpay-billing test still passes unchanged, since `RazorpayGateway`'s public contract (`IRazorpayGateway`) and observable behavior are identical.

- [ ] **Step 9: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions anywhere.

- [ ] **Step 10: Commit**

```bash
git add src/Sms.Shared.Kernel/Payments/RazorpayClient.cs src/Sms.Shared.Kernel/Payments/RazorpayGateway.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Unit/Payments/RazorpayClientTests.cs
git commit -m "refactor(payments): extract stateless RazorpayClient; RazorpayGateway now delegates"
```

---

### Task 3: `ITenantPaymentCredentialService` — encrypted per-tenant Razorpay credentials

**Files:**
- Create: `src/Sms.Modules.Finance/TenantPaymentCredentialRepository.cs` (raw-data repository, no encryption logic)
- Create: `src/Sms.Application/Services/Finance/TenantPaymentCredentialService.cs` (encryption/decryption + business rules)
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` (register `AddDataProtection()`, the repository, and the service)
- Test: `tests/Sms.Tests.Integration/Finance/TenantPaymentCredentialServiceTests.cs`

**Interfaces:**
- Consumes: `dbo.TenantPaymentCredentials` (Task 1).
- Produces: `ITenantPaymentCredentialService` with `Task<TenantRazorpayCredentials?> GetActiveAsync(Guid tenantId, CancellationToken ct)` (returns `null` if unconfigured or `IsEnabled == false`; otherwise decrypted `KeyId`/`KeySecret`/`WebhookSecret`/`Mode`), `Task<TenantRazorpayCredentialStatus> UpsertAsync(Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct)`, `Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct)` — used by Task 4/5/6 (`GetActiveAsync`) and Task 7 (`UpsertAsync`/`GetStatusAsync`).

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Dapper;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class TenantPaymentCredentialServiceTests(SqlServerFixture fx)
{
    private static ITenantPaymentCredentialService BuildService(SqlServerFixture fx)
    {
        var factory = new Sms.Shared.Kernel.Data.SqlConnectionFactory(fx.ConnectionString);
        var repo = new TenantPaymentCredentialRepository(factory);
        var provider = new ServiceCollection().AddDataProtection().Services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>();
        return new TenantPaymentCredentialService(repo, protector);
    }

    [Fact]
    public async Task Upsert_then_GetActive_round_trips_the_secret_decrypted()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);

        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            KeyId: "rzp_test_abc", KeySecret: "top-secret-value", WebhookSecret: "webhook-secret-value",
            Mode: "test", IsEnabled: true), CancellationToken.None);

        var active = await svc.GetActiveAsync(tenantId, CancellationToken.None);
        active.Should().NotBeNull();
        active!.KeyId.Should().Be("rzp_test_abc");
        active.KeySecret.Should().Be("top-secret-value");
        active.WebhookSecret.Should().Be("webhook-secret-value");
        active.Mode.Should().Be("test");

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var storedSecret = await conn.QuerySingleAsync<string>(
            "SELECT KeySecretEncrypted FROM dbo.TenantPaymentCredentials WHERE TenantId = @tenantId", new { tenantId });
        storedSecret.Should().NotBe("top-secret-value"); // must be encrypted at rest, not plaintext
    }

    [Fact]
    public async Task GetActive_returns_null_when_disabled()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_x", "secret", "whsecret", "test", IsEnabled: false), CancellationToken.None);

        (await svc.GetActiveAsync(tenantId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetActive_returns_null_when_never_configured()
    {
        var svc = BuildService(fx);
        (await svc.GetActiveAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_omitting_secret_leaves_stored_secret_unchanged()
    {
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var svc = BuildService(fx);
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_x", "original-secret", "original-webhook", "test", true), CancellationToken.None);

        // Second upsert changes only KeyId, omits both secrets (null = "leave unchanged")
        await svc.UpsertAsync(tenantId, new UpsertTenantRazorpayRequest(
            "rzp_test_y", null, null, "test", true), CancellationToken.None);

        var active = await svc.GetActiveAsync(tenantId, CancellationToken.None);
        active!.KeyId.Should().Be("rzp_test_y");
        active.KeySecret.Should().Be("original-secret");
        active.WebhookSecret.Should().Be("original-webhook");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TenantPaymentCredentialServiceTests"`
Expected: FAIL with "type or namespace ... could not be found" (nothing exists yet)

- [ ] **Step 3: Write the repository**

```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Finance;

public sealed record TenantPaymentCredentialRow(
    Guid TenantId, string Provider, string? KeyId, string? KeySecretEncrypted,
    string? WebhookSecretEncrypted, string Mode, bool IsEnabled);

public sealed class TenantPaymentCredentialRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<TenantPaymentCredentialRow?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TenantPaymentCredentialRow>(
            "SELECT TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled " +
            "FROM dbo.TenantPaymentCredentials WHERE TenantId = @tenantId AND Provider = 'razorpay'",
            new { tenantId }, ct)).FirstOrDefault();

    public Task UpsertAsync(
        Guid tenantId, string? keyId, string? keySecretEncrypted, string? webhookSecretEncrypted,
        string mode, bool isEnabled, bool hasNewKeySecret, bool hasNewWebhookSecret, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            """
            MERGE dbo.TenantPaymentCredentials AS target
            USING (SELECT @tenantId AS TenantId) AS src ON target.TenantId = src.TenantId AND target.Provider = 'razorpay'
            WHEN MATCHED THEN UPDATE SET
                KeyId = COALESCE(@keyId, target.KeyId),
                KeySecretEncrypted = CASE WHEN @hasNewKeySecret = 1 THEN @keySecretEncrypted ELSE target.KeySecretEncrypted END,
                WebhookSecretEncrypted = CASE WHEN @hasNewWebhookSecret = 1 THEN @webhookSecretEncrypted ELSE target.WebhookSecretEncrypted END,
                Mode = @mode, IsEnabled = @isEnabled, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled)
                VALUES (@tenantId, 'razorpay', @keyId, @keySecretEncrypted, @webhookSecretEncrypted, @mode, @isEnabled);
            """,
            new { tenantId, keyId, keySecretEncrypted, webhookSecretEncrypted, mode, isEnabled, hasNewKeySecret, hasNewWebhookSecret },
            ct);
}
```

- [ ] **Step 4: Write the service**

```csharp
using Microsoft.AspNetCore.DataProtection;
using Sms.Modules.Finance;

namespace Sms.Application.Services.Finance;

public sealed record TenantRazorpayCredentials(string KeyId, string KeySecret, string WebhookSecret, string Mode);
public sealed record UpsertTenantRazorpayRequest(string? KeyId, string? KeySecret, string? WebhookSecret, string Mode, bool IsEnabled);
public sealed record TenantRazorpayCredentialStatus(
    bool Enabled, string? KeyId, string Mode, string Status, bool KeySecretSet, bool WebhookSecretSet);

public interface ITenantPaymentCredentialService
{
    Task<TenantRazorpayCredentials?> GetActiveAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantRazorpayCredentialStatus> UpsertAsync(Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct = default);
    Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class TenantPaymentCredentialService(
    TenantPaymentCredentialRepository repo, IDataProtectionProvider dataProtection) : ITenantPaymentCredentialService
{
    private const string Purpose = "TenantPaymentCredentials.Razorpay.v1";
    private IDataProtector Protector => dataProtection.CreateProtector(Purpose);

    public async Task<TenantRazorpayCredentials?> GetActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await repo.GetAsync(tenantId, ct);
        if (row is null || !row.IsEnabled || row.KeyId is null || row.KeySecretEncrypted is null)
            return null;
        return new TenantRazorpayCredentials(
            row.KeyId,
            Protector.Unprotect(row.KeySecretEncrypted),
            row.WebhookSecretEncrypted is null ? "" : Protector.Unprotect(row.WebhookSecretEncrypted),
            row.Mode);
    }

    public async Task<TenantRazorpayCredentialStatus> UpsertAsync(
        Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct = default)
    {
        var hasNewKeySecret = !string.IsNullOrEmpty(req.KeySecret);
        var hasNewWebhookSecret = !string.IsNullOrEmpty(req.WebhookSecret);
        await repo.UpsertAsync(
            tenantId, req.KeyId,
            hasNewKeySecret ? Protector.Protect(req.KeySecret!) : null,
            hasNewWebhookSecret ? Protector.Protect(req.WebhookSecret!) : null,
            req.Mode, req.IsEnabled, hasNewKeySecret, hasNewWebhookSecret, ct);
        return await GetStatusAsync(tenantId, ct);
    }

    public async Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await repo.GetAsync(tenantId, ct);
        if (row is null)
            return new TenantRazorpayCredentialStatus(false, null, "test", "not_configured", false, false);
        var configured = row.KeyId is not null && row.KeySecretEncrypted is not null;
        return new TenantRazorpayCredentialStatus(
            row.IsEnabled, row.KeyId, row.Mode, configured ? "configured" : "not_configured",
            row.KeySecretEncrypted is not null, row.WebhookSecretEncrypted is not null);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, add (near the other `AddSingleton`/`AddScoped` calls, e.g. right after the `IAuditLogger` registration at line 109):

```csharp
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<Sms.Modules.Finance.TenantPaymentCredentialRepository>();
        builder.Services.AddScoped<ITenantPaymentCredentialService, TenantPaymentCredentialService>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TenantPaymentCredentialServiceTests"`
Expected: PASS (all 4 tests)

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Modules.Finance/TenantPaymentCredentialRepository.cs src/Sms.Application/Services/Finance/TenantPaymentCredentialService.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Finance/TenantPaymentCredentialServiceTests.cs
git commit -m "feat(fees): encrypted per-tenant Razorpay credential storage"
```

---

### Task 4: Order creation — `FeeOnlinePaymentService.CreateOrderAsync` + `POST /fees/invoices/{id}/razorpay/order`

**Files:**
- Create: `src/Sms.Application/Services/Finance/FeeOnlinePaymentService.cs`
- Create: `src/Sms.Modules.Finance/FeePaymentOrderRepository.cs`
- Modify: `src/Sms.Api/Controllers/FeeController.cs` (new action)
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` (register the new service/repository)
- Test: `tests/Sms.Tests.Integration/Finance/RazorpayOrderCreationTests.cs`

**Interfaces:**
- Consumes: `ITenantPaymentCredentialService.GetActiveAsync` (Task 3), `IRazorpayClient.CreateOrderAsync` (Task 2), `ITenantFeatureSet.Has` + `FeatureCatalog.OnlineFeePayment` (Task 1), `IFeeService.GetInvoiceAsync` (existing).
- Produces: `IFeeOnlinePaymentService.CreateOrderAsync(Guid invoiceId, bool initiatedByStaff, CancellationToken ct) -> ApiResult<RazorpayOrderResponse>` where `RazorpayOrderResponse(string OrderId, long Amount, string Currency, string KeyId, string? PayLink)` — consumed directly by Task 5's controller action pattern and by the acceptance tests in Task 8.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Services.Finance;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class RazorpayOrderCreationTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private sealed class FakeRazorpayClient : IRazorpayClient
    {
        public Task<RazorpayOrderCreated> CreateOrderAsync(
            string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct = default) =>
            Task.FromResult(new RazorpayOrderCreated($"order_fake_{amountPaise}", amountPaise, currency));
        public bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature) => true;
        public bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader) => true;
    }

    private static WebApplicationFactory<Program> App(SqlServerFixture fx) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services => services.AddSingleton<IRazorpayClient, FakeRazorpayClient>());
        });

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

    private static async Task<(Guid tenantId, Guid principalUserId, Guid studentId, Guid invoiceId)> SeedAsync(
        SqlServerFixture fx, decimal invoiceAmount = 5000m)
    {
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
            new { principalUserId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade) " +
            "VALUES (@studentId, @tenantId, 'A200', 'Kabir Shah', 'active', '6')",
            new { studentId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, Amount, PaidAmount, Status) " +
            "VALUES (@invoiceId, @tenantId, @studentId, 'Term 1', @invoiceAmount, 0, 'due')",
            new { invoiceId, tenantId, studentId, invoiceAmount });
        await conn.ExecuteAsync(
            "INSERT dbo.TenantPaymentCredentials (TenantId, Provider, KeyId, KeySecretEncrypted, Mode, IsEnabled) " +
            "VALUES (@tenantId, 'razorpay', 'rzp_test_seed', 'irrelevant-for-this-test-fake-client', 'test', 1)",
            new { tenantId });

        return (tenantId, principalUserId, studentId, invoiceId);
    }

    [Fact]
    public async Task Staff_can_create_an_order_for_the_full_remaining_balance_and_gets_a_pay_link()
    {
        await using var app = App(fx);
        var (tenantId, principalUserId, _, invoiceId) = await SeedAsync(fx, invoiceAmount: 4800m);
        var client = AuthedClient(app, tenantId, principalUserId, "principal");

        var res = await client.PostAsync($"/fees/invoices/{invoiceId}/razorpay/order", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("amount").GetInt64().Should().Be(480000); // 4800 rupees -> paise
        data.GetProperty("key_id").GetString().Should().Be("rzp_test_seed");
        data.GetProperty("order_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unlinked_parent_cannot_create_an_order_for_another_students_invoice()
    {
        await using var app = App(fx);
        var (tenantId, _, _, invoiceId) = await SeedAsync(fx);
        var unlinkedParentUserId = Guid.NewGuid();
        var client = AuthedClient(app, tenantId, unlinkedParentUserId, "parent");

        var res = await client.PostAsync($"/fees/invoices/{invoiceId}/razorpay/order", null);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Order_creation_fails_when_invoice_is_already_fully_paid()
    {
        await using var app = App(fx);
        var (tenantId, principalUserId, studentId, invoiceId) = await SeedAsync(fx, invoiceAmount: 1000m);
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "UPDATE dbo.FeeInvoices SET PaidAmount = 1000, Status = 'paid' WHERE Id = @invoiceId", new { invoiceId });
        var client = AuthedClient(app, tenantId, principalUserId, "principal");

        var res = await client.PostAsync($"/fees/invoices/{invoiceId}/razorpay/order", null);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Order_creation_fails_when_credentials_are_not_configured()
    {
        await using var app = App(fx);
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
                new { principalUserId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade) " +
                "VALUES (@studentId, @tenantId, 'A201', 'No Creds', 'active', '6')", new { studentId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, Amount, PaidAmount, Status) " +
                "VALUES (@invoiceId, @tenantId, @studentId, 'Term 1', 1000, 0, 'due')", new { invoiceId, tenantId, studentId });
            // deliberately NOT inserting TenantPaymentCredentials
        }
        var client = AuthedClient(app, tenantId, principalUserId, "principal");

        var res = await client.PostAsync($"/fees/invoices/{invoiceId}/razorpay/order", null);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayOrderCreationTests"`
Expected: FAIL — `POST /fees/invoices/{id}/razorpay/order` returns 404 (route doesn't exist)

- [ ] **Step 3: Write the order repository**

```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Finance;

public sealed record FeePaymentOrderRow(
    Guid Id, Guid TenantId, Guid InvoiceId, string RazorpayOrderId, long AmountPaise, string Status, string InitiatedBy);

public sealed class FeePaymentOrderRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task CreateAsync(
        Guid id, Guid tenantId, Guid invoiceId, string razorpayOrderId, long amountPaise, string initiatedBy,
        CancellationToken ct = default) =>
        ExecuteInlineAsync(
            "INSERT dbo.FeePaymentOrders (Id, TenantId, InvoiceId, RazorpayOrderId, AmountPaise, Status, InitiatedBy) " +
            "VALUES (@id, @tenantId, @invoiceId, @razorpayOrderId, @amountPaise, 'Created', @initiatedBy)",
            new { id, tenantId, invoiceId, razorpayOrderId, amountPaise, initiatedBy }, ct);

    public async Task<FeePaymentOrderRow?> GetByOrderIdAsync(string razorpayOrderId, CancellationToken ct = default) =>
        (await QueryInlineAsync<FeePaymentOrderRow>(
            "SELECT Id, TenantId, InvoiceId, RazorpayOrderId, AmountPaise, Status, InitiatedBy FROM dbo.FeePaymentOrders " +
            "WHERE RazorpayOrderId = @razorpayOrderId", new { razorpayOrderId }, ct)).FirstOrDefault();

    public Task MarkStatusAsync(Guid id, string status, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            "UPDATE dbo.FeePaymentOrders SET Status = @status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, status }, ct);
}
```

- [ ] **Step 4: Write `FeeOnlinePaymentService.CreateOrderAsync`**

```csharp
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public sealed record RazorpayOrderResponse(string OrderId, long Amount, string Currency, string KeyId, string? PayLink);

public interface IFeeOnlinePaymentService
{
    Task<ApiResult<RazorpayOrderResponse>> CreateOrderAsync(Guid invoiceId, bool initiatedByStaff, CancellationToken ct = default);
}

public sealed class FeeOnlinePaymentService(
    IFeeService fees,
    ITenantPaymentCredentialService credentials,
    IRazorpayClient razorpay,
    FeePaymentOrderRepository orders,
    ITenantContext tenant,
    ITenantFeatureSet features) : IFeeOnlinePaymentService
{
    private bool OnlinePaymentAllowed => tenant.IsPlatform || features.Has(FeatureCatalog.OnlineFeePayment);

    public async Task<ApiResult<RazorpayOrderResponse>> CreateOrderAsync(
        Guid invoiceId, bool initiatedByStaff, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!OnlinePaymentAllowed)
            return ApiResult<RazorpayOrderResponse>.Fail(
                new Error("feature_locked", $"This feature ({FeatureCatalog.OnlineFeePayment}) is not available on your plan."), 403);

        var inv = await fees.GetInvoiceAsync(invoiceId, ct);
        if (inv is null)
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("not_found", "resource not found"), 404);

        var remaining = Math.Max(0, inv.Amount - inv.PaidAmount);
        if (remaining <= 0 || string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("conflict", "invoice already paid"), 409);

        var creds = await credentials.GetActiveAsync(tid, ct);
        if (creds is null)
            return ApiResult<RazorpayOrderResponse>.Fail(
                new Error("payment_gateway_not_configured", "Online payment is not available for this school yet."), 403);

        var amountPaise = (long)(remaining * 100m);
        var order = await razorpay.CreateOrderAsync(
            creds.KeyId, creds.KeySecret, amountPaise, "INR", $"invoice-{invoiceId:N}", ct);

        var orderRowId = Guid.NewGuid();
        var initiatedBy = initiatedByStaff ? "staff" : "parent";
        await orders.CreateAsync(orderRowId, tid, invoiceId, order.OrderId, amountPaise, initiatedBy, ct);

        string? payLink = null; // Payment-link generation is a small follow-up enhancement; omitted for v1 per the spec's staff-convenience note.

        return ApiResult<RazorpayOrderResponse>.Ok(
            new RazorpayOrderResponse(order.OrderId, order.AmountPaise, order.Currency, creds.KeyId, payLink));
    }
}
```

Note: `pay_link` generation via Razorpay's Payment Links API is deliberately deferred — the response contract already includes the (nullable) field so `sms-admin`'s existing "Send pay link" button degrades gracefully (its own code already handles `order.payLink` being absent, per Task 9). Add a follow-up note to the plan's final task acknowledging this as a known, explicitly-scoped-out enhancement rather than a bug.

- [ ] **Step 5: Wire the controller action**

In `src/Sms.Api/Controllers/FeeController.cs`, add near the existing `PayInvoice` action (reusing its exact guard):

```csharp
    [HttpPost("fees/invoices/{id:guid}/razorpay/order")]
    public async Task<IActionResult> CreateRazorpayOrder(Guid id, CancellationToken ct)
    {
        var inv = await fees.GetInvoiceAsync(id, ct);
        if (inv is null)
            return NotFoundResult();
        var isStaff = RoleChecks.IsStaff(User);
        if (!isStaff && !await sis.IsLinkedToCallerAsync(inv.StudentId, ct))
            return ForbiddenResult("not your linked student");
        return FromResult(await onlinePayments.CreateOrderAsync(id, isStaff, ct));
    }
```

Add `IFeeOnlinePaymentService onlinePayments` to `FeeController`'s primary constructor parameter list (alongside the existing `IFeeService fees, ISisService sis`).

- [ ] **Step 6: Register in DI**

In `ServiceCollectionExtensions.cs`, alongside Task 3's registrations:

```csharp
        builder.Services.AddScoped<Sms.Modules.Finance.FeePaymentOrderRepository>();
        builder.Services.AddScoped<IFeeOnlinePaymentService, FeeOnlinePaymentService>();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayOrderCreationTests"`
Expected: PASS (all 4 tests)

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions (`FeeController`'s constructor gained a parameter — check any existing `FeeController` unit-construction tests, if any, are updated to supply it).

- [ ] **Step 9: Commit**

```bash
git add src/Sms.Application/Services/Finance/FeeOnlinePaymentService.cs src/Sms.Modules.Finance/FeePaymentOrderRepository.cs src/Sms.Api/Controllers/FeeController.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Finance/RazorpayOrderCreationTests.cs
git commit -m "feat(fees): Razorpay order creation endpoint"
```

---

### Task 5: Verify — `FeeOnlinePaymentService.VerifyAsync` + `POST /fees/invoices/{id}/razorpay/verify`

**Files:**
- Modify: `src/Sms.Application/Services/Finance/FeeOnlinePaymentService.cs` (add `VerifyAsync`)
- Modify: `src/Sms.Api/Controllers/FeeController.cs` (new action)
- Test: `tests/Sms.Tests.Integration/Finance/RazorpayVerifyPaymentTests.cs`

**Interfaces:**
- Consumes: `FeePaymentOrderRepository.GetByOrderIdAsync`/`MarkStatusAsync` (Task 4), `ITenantPaymentCredentialService.GetActiveAsync` (Task 3), `IRazorpayClient.VerifyPaymentSignature` (Task 2), `IFeeService.PayInvoiceAsync` (existing — the reuse point).
- Produces: `IFeeOnlinePaymentService.VerifyAsync(Guid invoiceId, RazorpayVerifyRequest req, CancellationToken ct) -> ApiResult<FeePaymentResponse>` where `RazorpayVerifyRequest(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature)` — consumed by Task 8's acceptance tests and mirrors what Task 6's webhook handler independently triggers via the same `IFeeService.PayInvoiceAsync` call.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Common;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class RazorpayVerifyPaymentTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private sealed class FakeRazorpayClient(bool signatureValid) : IRazorpayClient
    {
        public Task<RazorpayOrderCreated> CreateOrderAsync(
            string keyId, string keySecret, long amountPaise, string currency, string receipt, CancellationToken ct = default) =>
            Task.FromResult(new RazorpayOrderCreated($"order_fake_{Guid.NewGuid():N}", amountPaise, currency));
        public bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature) => signatureValid;
        public bool VerifyWebhookSignature(string webhookSecret, string body, string signatureHeader) => signatureValid;
    }

    private sealed class CapturingAnnouncementService : IAnnouncementService
    {
        public List<CreateAnnouncementRequest> Created { get; } = [];

        public Task<ApiResult<IReadOnlyList<AnnouncementResponse>>> ListAsync(string? audience, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<AnnouncementResponse>>.Ok(Array.Empty<AnnouncementResponse>()));

        public Task<ApiResult<AnnouncementResponse>> CreateAsync(
            CreateAnnouncementRequest req, Guid? creatorUserId, string? role, CancellationToken ct = default)
        {
            Created.Add(req);
            return Task.FromResult(ApiResult<AnnouncementResponse>.Ok(
                new AnnouncementResponse(Guid.NewGuid(), Guid.Empty, req.Title, req.Body, DateTime.UtcNow, null, role, req.Type ?? "general", false, req.Audience)));
        }
    }

    private static (WebApplicationFactory<Program> app, CapturingAnnouncementService announcements) App(
        SqlServerFixture fx, bool signatureValid = true)
    {
        var fake = new CapturingAnnouncementService();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IRazorpayClient>(new FakeRazorpayClient(signatureValid));
                services.AddScoped<IAnnouncementService>(_ => fake);
            });
        });
        return (app, fake);
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

    private static async Task<(Guid tenantId, Guid principalUserId, Guid invoiceId, Guid studentId)> SeedAsync(
        SqlServerFixture fx, decimal invoiceAmount = 4800m, string guardianEmail = "guardian@school.test")
    {
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
            new { principalUserId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status, Grade, GuardianEmail) " +
            "VALUES (@studentId, @tenantId, 'A300', 'Meera Rao', 'active', '7', @guardianEmail)",
            new { studentId, tenantId, guardianEmail });
        await conn.ExecuteAsync(
            "INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, Amount, PaidAmount, Status) " +
            "VALUES (@invoiceId, @tenantId, @studentId, 'Term 1', @invoiceAmount, 0, 'due')",
            new { invoiceId, tenantId, studentId, invoiceAmount });
        await conn.ExecuteAsync(
            "INSERT dbo.TenantPaymentCredentials (TenantId, Provider, KeyId, KeySecretEncrypted, Mode, IsEnabled) " +
            "VALUES (@tenantId, 'razorpay', 'rzp_test_seed', 'enc', 'test', 1)", new { tenantId });
        return (tenantId, principalUserId, invoiceId, studentId);
    }

    private static async Task<string> CreateOrderAsync(HttpClient client, Guid invoiceId)
    {
        var res = await client.PostAsync($"/fees/invoices/{invoiceId}/razorpay/order", null);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("order_id").GetString()!;
    }

    [Fact]
    public async Task Verify_records_payment_marks_invoice_paid_and_notifies_guardian_exactly_once()
    {
        var (app, announcements) = App(fx);
        await using var _ = app;
        var (tenantId, principalUserId, invoiceId, _) = await SeedAsync(fx);
        var client = AuthedClient(app, tenantId, principalUserId, "principal");
        var orderId = await CreateOrderAsync(client, invoiceId);

        var res = await client.PostAsJsonAsync($"/fees/invoices/{invoiceId}/razorpay/verify", new
        {
            razorpay_order_id = orderId, razorpay_payment_id = "pay_ABC123", razorpay_signature = "any-value-fake-client-accepts-everything",
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("method").GetString().Should().Be("Razorpay");

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var status = await conn.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.FeeInvoices WHERE Id = @invoiceId", new { invoiceId });
        status.Should().Be("paid");
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE InvoiceId = @invoiceId", new { invoiceId });
        paymentCount.Should().Be(1);

        announcements.Created.Should().ContainSingle(); // the existing Phase 1 guardian notification, reused unchanged for Razorpay
    }

    [Fact]
    public async Task Duplicate_verify_with_the_same_payment_id_does_not_create_a_second_payment_or_a_second_notification()
    {
        var (app, announcements) = App(fx);
        await using var _ = app;
        var (tenantId, principalUserId, invoiceId, _) = await SeedAsync(fx);
        var client = AuthedClient(app, tenantId, principalUserId, "principal");
        var orderId = await CreateOrderAsync(client, invoiceId);
        var body = new { razorpay_order_id = orderId, razorpay_payment_id = "pay_DUPLICATE", razorpay_signature = "x" };

        var first = await client.PostAsJsonAsync($"/fees/invoices/{invoiceId}/razorpay/verify", body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await client.PostAsJsonAsync($"/fees/invoices/{invoiceId}/razorpay/verify", body);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE InvoiceId = @invoiceId", new { invoiceId });
        paymentCount.Should().Be(1);
        announcements.Created.Should().ContainSingle(); // the replay must not notify a second time
    }

    [Fact]
    public async Task Tampered_signature_is_rejected_and_no_payment_is_recorded()
    {
        var (app, _) = App(fx, signatureValid: false);
        await using var __ = app;
        var (tenantId, principalUserId, invoiceId, _) = await SeedAsync(fx);
        var client = AuthedClient(app, tenantId, principalUserId, "principal");
        var orderId = await CreateOrderAsync(client, invoiceId);

        var res = await client.PostAsJsonAsync($"/fees/invoices/{invoiceId}/razorpay/verify", new
        {
            razorpay_order_id = orderId, razorpay_payment_id = "pay_TAMPERED", razorpay_signature = "bad",
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE InvoiceId = @invoiceId", new { invoiceId });
        paymentCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayVerifyPaymentTests"`
Expected: FAIL — route doesn't exist yet (404)

- [ ] **Step 3: Add `VerifyAsync` to `FeeOnlinePaymentService`**

Append to the interface and class from Task 4:

```csharp
    // added to IFeeOnlinePaymentService:
    Task<ApiResult<FeePaymentResponse>> VerifyAsync(Guid invoiceId, RazorpayVerifyRequest req, CancellationToken ct = default);
```

```csharp
public sealed record RazorpayVerifyRequest(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);
```

```csharp
    public async Task<ApiResult<FeePaymentResponse>> VerifyAsync(
        Guid invoiceId, RazorpayVerifyRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var order = await orders.GetByOrderIdAsync(req.RazorpayOrderId, ct);
        if (order is null || order.TenantId != tid || order.InvoiceId != invoiceId)
            return ApiResult<FeePaymentResponse>.Fail(new Error("not_found", "no matching order for this invoice"), 404);

        var creds = await credentials.GetActiveAsync(tid, ct);
        if (creds is null)
            return ApiResult<FeePaymentResponse>.Fail(
                new Error("payment_gateway_not_configured", "Online payment is not available for this school."), 403);

        if (!razorpay.VerifyPaymentSignature(creds.KeySecret, req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature))
            return ApiResult<FeePaymentResponse>.Fail(new Error("invalid_signature", "Payment signature could not be verified"), 400);

        var payment = await fees.PayInvoiceAsync(
            invoiceId,
            new PayFeeInvoiceRequest(
                Amount: order.AmountPaise / 100m,
                Method: "Razorpay",
                Mode: null,
                Ref: req.RazorpayPaymentId,
                StudentName: null, ClassLabel: null, Cls: null, FeeType: null, HeadId: null, HeadName: null,
                IdempotencyKey: DeterministicGuidFrom(req.RazorpayPaymentId)),
            ct);

        if (payment.Error is null)
            await orders.MarkStatusAsync(order.Id, "Captured", ct);

        return payment;
    }

    /// PayFeeInvoiceRequest.IdempotencyKey is a Guid, but Razorpay payment ids are opaque
    /// strings (e.g. "pay_ABC123") — deterministically derive a stable Guid from the payment id
    /// (MD5 of the UTF-8 bytes) so the SAME payment_id always maps to the SAME idempotency key,
    /// which is all RecordInvoicePaymentAsync's uniqueness guarantee actually requires.
    private static Guid DeterministicGuidFrom(string value) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
```

- [ ] **Step 4: Wire the controller action**

In `FeeController.cs`, add next to `CreateRazorpayOrder`:

```csharp
    [HttpPost("fees/invoices/{id:guid}/razorpay/verify")]
    public async Task<IActionResult> VerifyRazorpayPayment(Guid id, [FromBody] RazorpayVerifyBody req, CancellationToken ct)
    {
        var inv = await fees.GetInvoiceAsync(id, ct);
        if (inv is null)
            return NotFoundResult();
        if (!RoleChecks.IsStaff(User) && !await sis.IsLinkedToCallerAsync(inv.StudentId, ct))
            return ForbiddenResult("not your linked student");
        return FromResult(await onlinePayments.VerifyAsync(
            id, new RazorpayVerifyRequest(req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature), ct));
    }
```

Add the wire-shape DTO (snake_case-bound via the existing model-binding convention used elsewhere in this controller — check `PayFeeInvoiceRequest`'s own JSON attributes/naming convention and match it exactly) near the top of `FeeController.cs` or in `Sms.Modules.Finance` alongside the other request records:

```csharp
public sealed record RazorpayVerifyBody(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayVerifyPaymentTests"`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Application/Services/Finance/FeeOnlinePaymentService.cs src/Sms.Api/Controllers/FeeController.cs tests/Sms.Tests.Integration/Finance/RazorpayVerifyPaymentTests.cs
git commit -m "feat(fees): Razorpay payment verification, reusing PayInvoiceAsync"
```

---

### Task 6: Webhook — `POST /v1/webhooks/razorpay-fees`

**Files:**
- Create: `src/Sms.Api/Controllers/RazorpayFeeWebhookController.cs`
- Test: `tests/Sms.Tests.Integration/Finance/RazorpayFeeWebhookTests.cs`

**Interfaces:**
- Consumes: `FeePaymentOrderRepository.GetByOrderIdAsync`/`MarkStatusAsync` (Task 4), `ITenantPaymentCredentialService.GetActiveAsync` (Task 3), `IRazorpayClient.VerifyWebhookSignature` (Task 2), `IFeeService.PayInvoiceAsync` (existing).
- Produces: an `[AllowAnonymous]` webhook endpoint — no new interface consumed by later tasks, this is a leaf.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
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
            b.ConfigureTestServices(services => services.AddSingleton<IRazorpayClient>(new FakeRazorpayClient(signatureValid)));
        });

    private static async Task<(Guid tenantId, Guid invoiceId, string orderId)> SeedOrderAsync(SqlServerFixture fx)
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
        await conn.ExecuteAsync(
            "INSERT dbo.TenantPaymentCredentials (TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled) " +
            "VALUES (@tenantId, 'razorpay', 'rzp_test_wh', 'enc', 'enc-webhook', 'test', 1)", new { tenantId });
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
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(fx);
        var client = app.CreateClient();

        var res = await client.PostAsync("/v1/webhooks/razorpay-fees",
            WebhookBody(orderId, "pay_WEBHOOK_ONLY"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var status = await conn.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.FeeInvoices WHERE Id = @invoiceId", new { invoiceId });
        status.Should().Be("paid");
    }

    [Fact]
    public async Task Webhook_after_client_verify_already_processed_it_does_not_duplicate()
    {
        await using var app = App(fx);
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(fx);
        var client = app.CreateClient();
        const string paymentId = "pay_RACE";

        var first = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, paymentId));
        var second = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, paymentId));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE InvoiceId = @invoiceId", new { invoiceId });
        paymentCount.Should().Be(1);
    }

    [Fact]
    public async Task Wrong_secret_webhook_signature_is_rejected_with_no_state_change()
    {
        await using var app = App(fx, signatureValid: false);
        var (tenantId, invoiceId, orderId) = await SeedOrderAsync(fx);
        var client = app.CreateClient();

        var res = await client.PostAsync("/v1/webhooks/razorpay-fees", WebhookBody(orderId, "pay_BADSIG"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayFeeWebhookTests"`
Expected: FAIL — 404, controller doesn't exist

- [ ] **Step 3: Write the webhook controller**

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Payments;

namespace Sms.Api.Controllers;

[Route("v1/webhooks")]
[AllowAnonymous]
public sealed class RazorpayFeeWebhookController(
    FeePaymentOrderRepository orders,
    ITenantPaymentCredentialService credentials,
    IRazorpayClient razorpay,
    IFeeService fees) : ApiControllerBase
{
    [HttpPost("razorpay-fees")]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        string orderId;
        string paymentId;
        string eventName;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            eventName = doc.RootElement.GetProperty("event").GetString() ?? "";
            var entity = doc.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            orderId = entity.GetProperty("order_id").GetString() ?? "";
            paymentId = entity.GetProperty("id").GetString() ?? "";
        }
        catch (Exception)
        {
            return Ok(); // malformed payload — nothing we can act on, acknowledge so Razorpay doesn't retry forever
        }

        // order_id is read here ONLY as an untrusted lookup key to find which tenant's secret to
        // try — no state changes happen until the full body's signature verifies below.
        var order = await orders.GetByOrderIdAsync(orderId, ct);
        if (order is null)
            return Ok(); // unknown order — acknowledge, nothing to do, avoid endless retries

        var creds = await credentials.GetActiveAsync(order.TenantId, ct);
        if (creds is null || !razorpay.VerifyWebhookSignature(creds.WebhookSecret, rawBody, Request.Headers["X-Razorpay-Signature"].ToString()))
            return BadRequest();

        if (eventName == "payment.failed")
        {
            await orders.MarkStatusAsync(order.Id, "Failed", ct);
            return Ok();
        }
        if (eventName != "payment.captured" && eventName != "order.paid")
            return Ok(); // an event type we don't act on

        var payment = await fees.PayInvoiceAsync(
            order.InvoiceId,
            new PayFeeInvoiceRequest(
                Amount: order.AmountPaise / 100m,
                Method: "Razorpay",
                Mode: null,
                Ref: paymentId,
                StudentName: null, ClassLabel: null, Cls: null, FeeType: null, HeadId: null, HeadName: null,
                IdempotencyKey: DeterministicGuidFrom(paymentId)),
            ct);

        if (payment.Error is null)
            await orders.MarkStatusAsync(order.Id, "Captured", ct);

        return Ok();
    }

    private static Guid DeterministicGuidFrom(string value) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
```

Note: the `DeterministicGuidFrom` helper is duplicated between `FeeOnlinePaymentService` and this controller. Before committing, extract it to a single shared internal static helper (e.g. a small `RazorpayIdempotency` static class in `Sms.Shared.Kernel.Payments`) and have both call sites use it, so the two independent confirmation paths are guaranteed to derive the *same* Guid for the *same* payment id (this is load-bearing for the idempotency guarantee — do not skip the dedup).

- [ ] **Step 4: Deduplicate the helper (do this before running tests)**

Create `src/Sms.Shared.Kernel/Payments/RazorpayIdempotency.cs`:

```csharp
namespace Sms.Shared.Kernel.Payments;

/// PayFeeInvoiceRequest.IdempotencyKey is a Guid, but Razorpay payment ids are opaque strings
/// (e.g. "pay_ABC123"). Both the client-verify path and the webhook path must derive the SAME
/// Guid from the SAME payment id so a race between them is caught by the existing
/// (TenantId, IdempotencyKey) unique constraint — hence one shared, deterministic derivation.
public static class RazorpayIdempotency
{
    public static Guid KeyFor(string razorpayPaymentId) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(razorpayPaymentId)));
}
```

Replace both private `DeterministicGuidFrom` methods (in `FeeOnlinePaymentService.VerifyAsync` from Task 5, and in this controller) with calls to `RazorpayIdempotency.KeyFor(paymentId)`, and delete both private methods.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayFeeWebhookTests"`
Expected: PASS (all 4 tests)

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Api/Controllers/RazorpayFeeWebhookController.cs src/Sms.Shared.Kernel/Payments/RazorpayIdempotency.cs src/Sms.Application/Services/Finance/FeeOnlinePaymentService.cs tests/Sms.Tests.Integration/Finance/RazorpayFeeWebhookTests.cs
git commit -m "feat(fees): Razorpay payment webhook, idempotency-key derivation shared with verify"
```

---

### Task 7: `SchoolIntegrationsController` — Owner-only credential configuration

**Files:**
- Create: `src/Sms.Api/Controllers/SchoolIntegrationsController.cs`
- Test: `tests/Sms.Tests.Integration/Finance/SchoolIntegrationsControllerTests.cs`

**Interfaces:**
- Consumes: `ITenantPaymentCredentialService` (Task 3), `Policies.SchoolOwnerOnly` (Task 1).
- Produces: `GET/PUT /school/integrations`, `POST /school/integrations/razorpay/verify` — leaf endpoints, no later task depends on them.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class SchoolIntegrationsControllerTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

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

    private static WebApplicationFactory<Program> App(SqlServerFixture fx) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    [Fact]
    public async Task Owner_can_configure_razorpay_and_secret_is_never_echoed_back()
    {
        await using var app = App(fx);
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var client = AuthedClient(app, tenantId, Guid.NewGuid(), "school.owner");

        var put = await client.PutAsJsonAsync("/school/integrations", new
        {
            razorpay = new { key_id = "rzp_test_owner", key_secret = "sekrit", webhook_secret = "whsekrit", mode = "test", enabled = true },
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync("/school/integrations");
        using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var razorpay = doc.RootElement.GetProperty("data").GetProperty("razorpay");
        razorpay.GetProperty("key_id").GetString().Should().Be("rzp_test_owner");
        razorpay.GetProperty("key_secret_set").GetBoolean().Should().BeTrue();
        razorpay.TryGetProperty("key_secret", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Principal_is_forbidden_from_configuring_razorpay()
    {
        await using var app = App(fx);
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var client = AuthedClient(app, tenantId, Guid.NewGuid(), "principal");

        var res = await client.PutAsJsonAsync("/school/integrations", new { razorpay = new { key_id = "x" } });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Verify_action_reports_configured_status()
    {
        await using var app = App(fx);
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var client = AuthedClient(app, tenantId, Guid.NewGuid(), "school.owner");
        await client.PutAsJsonAsync("/school/integrations", new
        {
            razorpay = new { key_id = "rzp_test_v", key_secret = "s", webhook_secret = "w", mode = "test", enabled = true },
        });

        var res = await client.PostAsync("/school/integrations/razorpay/verify", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("status").GetString().Should().BeOneOf("configured", "invalid");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SchoolIntegrationsControllerTests"`
Expected: FAIL — routes don't exist (404)

- [ ] **Step 3: Write the controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Controllers;

public sealed record RazorpaySettingsBody(string? KeyId, string? KeySecret, string? WebhookSecret, string? Mode, bool? Enabled);
public sealed record SaveIntegrationsBody(RazorpaySettingsBody? Razorpay);

[Route("school/integrations")]
[Authorize]
public sealed class SchoolIntegrationsController(ITenantPaymentCredentialService credentials, ITenantContext tenant) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        var status = await credentials.GetStatusAsync(tid, ct);
        return Ok(new
        {
            data = new
            {
                email = new { }, // Email integration backend is a separate future spec — placeholder shape only.
                sms = new { },   // Same for SMS.
                razorpay = new
                {
                    enabled = status.Enabled,
                    key_id = status.KeyId ?? "",
                    mode = status.Mode,
                    status = status.Status,
                    key_secret_set = status.KeySecretSet,
                    webhook_secret_set = status.WebhookSecretSet,
                },
            },
        });
    }

    [HttpPut("")]
    [Authorize(Policy = Policies.SchoolOwnerOnly)]
    public async Task<IActionResult> Save([FromBody] SaveIntegrationsBody body, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        if (body.Razorpay is { } r)
            await credentials.UpsertAsync(tid, new UpsertTenantRazorpayRequest(
                r.KeyId, r.KeySecret, r.WebhookSecret, r.Mode ?? "test", r.Enabled ?? false), ct);
        return await Get(ct);
    }

    [HttpPost("razorpay/verify")]
    [Authorize(Policy = Policies.SchoolOwnerOnly)]
    public async Task<IActionResult> VerifyCredentials(CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        var status = await credentials.GetStatusAsync(tid, ct);
        // A real Razorpay API ping (e.g. GET /v1/orders?count=1 with Basic auth) belongs here once
        // this endpoint needs to distinguish "configured" from "invalid" against the live API —
        // deferred: today it reports "configured" whenever both KeyId and KeySecret are stored,
        // matching GetStatusAsync's existing definition, and never returns "invalid" yet.
        return Ok(new { data = new { status = status.Status } });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SchoolIntegrationsControllerTests"`
Expected: PASS (all 3 tests)

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Api/Controllers/SchoolIntegrationsController.cs tests/Sms.Tests.Integration/Finance/SchoolIntegrationsControllerTests.cs
git commit -m "feat(fees): Owner-only Razorpay credential settings endpoint"
```

---

### Task 8: Whole-flow acceptance tests + full regression pass (verification only, no new production code)

**Files:**
- Create: `tests/Sms.Tests.Integration/Finance/RazorpayFeePaymentAcceptanceTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: nothing new — this task is a final cross-cutting verification pass mapped to the spec's 12 numbered rules, run by the controller (you), not delegated.

- [ ] **Step 1: Write acceptance tests for the rules not already fully exercised by Tasks 4-7's own tests**

Add to `RazorpayFeePaymentAcceptanceTests.cs` (reuse the same `App`/`AuthedClient`/`SeedAsync` helper patterns from Tasks 4-6's test files — copy and adapt, since each task's implementer sees only their own task):

```csharp
// Rule 10: multi-child parent — verify child B's order/verify never touches child A's data.
[Fact]
public async Task Parent_with_two_children_can_only_act_on_the_child_they_selected() { /* seed 2 students under one guardian-linked parent user, one invoice each; create+verify an order for child B; assert child A's invoice/payments are untouched */ }

// Rule 11 (cross-tenant half not covered elsewhere): a staff/parent token from tenant A can never create or verify an order against tenant B's invoice, even by guessing the invoice id.
[Fact]
public async Task Cross_tenant_invoice_id_guess_is_rejected() { /* tenant A's authed client posts order/verify against tenant B's invoiceId; assert 404 (GetInvoiceAsync/order lookup is tenant-scoped) not a data leak */ }

// Feature-gate cross-check: a non-platinum tenant is blocked even with valid configured credentials.
[Fact]
public async Task Non_entitled_tier_cannot_create_an_order_even_with_credentials_configured() { /* TestTenancy.EnsureTenantAsync(..., tier: "silver"); assert 403 feature_locked */ }
```

Implement each test body following the exact seeding/auth patterns already established in Tasks 4-6 (`TestTenancy.EnsureTenantAsync`, direct SQL inserts for `Students`/`FeeInvoices`/`TenantPaymentCredentials`, `AuthedClient` helper) — do not invent a new pattern.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~RazorpayFeePaymentAcceptanceTests"`
Expected: PASS

- [ ] **Step 3: Run the entire backend test suite**

Run: `dotnet build && dotnet test`
Expected: 100% pass, zero regressions across the whole solution (Unit + Integration).

- [ ] **Step 4: Cross-check against the spec's acceptance table**

Re-read `docs/superpowers/specs/2026-09-09-razorpay-fee-payment-design.md`'s §8 test matrix table (rules 1-12) and confirm each row is now covered by a named test somewhere in Tasks 1-8 — update the ledger/progress notes (see Task 9's note on `.superpowers/sdd/` ledger conventions from the fee-invoice-notifications precedent) with the mapping.

- [ ] **Step 5: Commit**

```bash
git add tests/Sms.Tests.Integration/Finance/RazorpayFeePaymentAcceptanceTests.cs
git commit -m "test(fees): whole-flow Razorpay acceptance tests mapped to spec rules 1-12"
```

---

### Task 9: `sms-admin` — remove the duplicate client-side payment-success notification

**Files:**
- Modify: `src/screens/school/finance.tsx` (remove the two `notifyReceiptBestEffort(...)` call sites at the manual-payment success handler and the Razorpay `collectOnline` success handler)
- Modify: any existing test file covering these handlers (e.g. a `finance.test.tsx` asserting `notifyFeeAudience`/`notifyReceiptBestEffort` was called after payment — update or remove that assertion)

**Interfaces:**
- Consumes: nothing from the backend tasks directly (this is independent of Task 1-8's code, only reachable once they ship).
- Produces: nothing consumed by later tasks — this is a leaf cleanup.

- [ ] **Step 1: Locate and read the current test coverage for this behavior**

Run: `grep -rn "notifyReceiptBestEffort\|notifyFeeAudience" src/screens/school/*.test.tsx` (sms-admin repo) to find any test asserting the client-side notify fires after a successful payment. Read it fully before editing.

- [ ] **Step 2: Update/remove the test assertion**

If a test asserts `notifyFeeAudience`/`notifyReceiptBestEffort` is called after `payInvoice.mutate(...)` succeeds or after `collectOnline`/`sendPayLink` succeeds, rewrite it to assert the **opposite** — that no client-side notify call happens (the backend now owns this for every payment method, per Phase 1's `NotifyGuardianOnPaymentBestEffortAsync` extended to Razorpay in Task 5/6 above). Example shape (adapt to the actual existing test file's structure once read in Step 1):

```tsx
it('does not fire a client-side notification after a successful payment (the backend already does)', async () => {
  const notifyFeeAudienceMock = vi.fn()
  vi.mock('@/lib/feeNotify', () => ({ notifyFeeAudience: notifyFeeAudienceMock }))
  // ... render, record a payment via the existing test flow ...
  await waitFor(() => expect(payInvoiceMock).toHaveBeenCalled())
  expect(notifyFeeAudienceMock).not.toHaveBeenCalled()
})
```

- [ ] **Step 3: Run test to verify it fails against current code**

Run: `npm test -- finance` (sms-admin repo)
Expected: FAIL — the current code still calls `notifyReceiptBestEffort`, so the new "does not fire" assertion fails.

- [ ] **Step 4: Remove both call sites**

In `finance.tsx`, remove the `void notifyReceiptBestEffort(...)` call from the manual "Record" payment success handler (the `payInvoice.mutate({ ... }, { onSuccess: () => { ... } })` block that currently calls it, around line 216) and from the Razorpay `collectOnline` success handler (around line 1619). Leave the rest of each handler's success logic (toasts, query invalidation, receipt PDF download if any) unchanged — only the notify call itself is removed. Once `notifyReceiptBestEffort` and `notifyFeeAudience` have no remaining callers anywhere in the file/repo, also remove the now-dead `notifyReceiptBestEffort` function definition and its `notifyFeeAudience`/`feeNotify` import — but first `grep -rn "notifyFeeAudience\|notifyReceiptBestEffort" src/` across the whole `sms-admin` repo to confirm nothing else still calls it before deleting the function/import.

- [ ] **Step 5: Run test to verify it passes**

Run: `npm test -- finance`
Expected: PASS

- [ ] **Step 6: Run the whole sms-admin test suite**

Run: `npm test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/screens/school/finance.tsx src/screens/school/finance.test.tsx
git commit -m "fix(fees): remove duplicate client-side payment notification (backend now owns it for every method)"
```

---

### Task 10: `sms-student` — Razorpay order/verify API client + `FeesService` contract update

**Files:**
- Create: `src/services/http/razorpayFees.ts` (or add to the existing `dtos.ts`/http module structure — check `src/services/http/dtos.ts` for where `FeeInvoiceDTO` lives and add the new request/response DTOs alongside it)
- Modify: `src/services/http/index.ts` (replace the stub `pay:` implementation's shape — this task only adds the new API functions; Task 12 wires the screen to call them)
- Modify: `src/services/types.ts` (extend `FeesService` interface)
- Test: a new or extended test file matching this repo's existing test conventions for `services/http` (check for an existing `*.test.ts` alongside `index.ts` and follow its pattern; if none exists, create `src/services/http/razorpayFees.test.ts`)

**Interfaces:**
- Consumes: nothing new — this is a pure API-client layer.
- Produces: `FeesService.createRazorpayOrder(feeId: string): Promise<{ orderId: string; amount: number; currency: string; keyId: string }>`, `FeesService.verifyRazorpayPayment(feeId: string, body: { razorpayOrderId: string; razorpayPaymentId: string; razorpaySignature: string }): Promise<Fee>` — consumed by Task 11 (the checkout WebView component) and Task 12 (the screen wiring).

- [ ] **Step 1: Read the existing DTO/mapper conventions before writing anything**

Read `src/services/http/dtos.ts` (for `FeeInvoiceDTO`'s shape and any snake_case field naming) and `src/services/http/mappers.ts` (for `toFee`) fully, so the new DTOs/mapping match the established convention exactly rather than inventing a new one.

- [ ] **Step 2: Write the failing test**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { services } from '@/services';

describe('fees.createRazorpayOrder', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('POSTs /fees/invoices/{id}/razorpay/order and maps the snake_case order', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      data: { order_id: 'order_x', amount: 480000, currency: 'INR', key_id: 'rzp_test_school1' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);

    const order = await services.fees.createRazorpayOrder('INV-1');

    expect(String(fetchMock.mock.calls[0][0])).toContain('/fees/invoices/INV-1/razorpay/order');
    expect(order).toMatchObject({ orderId: 'order_x', amount: 480000, currency: 'INR', keyId: 'rzp_test_school1' });
  });
});

describe('fees.verifyRazorpayPayment', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('POSTs /fees/invoices/{id}/razorpay/verify with a snake_case body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      data: { id: 1, invoice_id: 'INV-1', student_id: 's1', student_name: 'Asha', amount: 4800, status: 'paid' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);

    await services.fees.verifyRazorpayPayment('INV-1', {
      razorpayOrderId: 'order_x', razorpayPaymentId: 'pay_x', razorpaySignature: 'sig',
    });

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/fees/invoices/INV-1/razorpay/verify');
    const body = JSON.parse((init as RequestInit).body as string);
    expect(body).toMatchObject({ razorpay_order_id: 'order_x', razorpay_payment_id: 'pay_x', razorpay_signature: 'sig' });
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npm test -- razorpayFees` (or the actual test filename chosen in Step 1's read)
Expected: FAIL — `services.fees.createRazorpayOrder`/`verifyRazorpayPayment` don't exist

- [ ] **Step 4: Extend `FeesService` and its implementation**

In `src/services/types.ts`:

```typescript
export interface RazorpayOrder {
  orderId: string;
  amount: number;
  currency: string;
  keyId: string;
}

export interface FeesService {
  list(childId: string): Promise<Fee[]>;
  pay(feeId: string): Promise<Fee>;
  createRazorpayOrder(feeId: string): Promise<RazorpayOrder>;
  verifyRazorpayPayment(
    feeId: string,
    body: { razorpayOrderId: string; razorpayPaymentId: string; razorpaySignature: string },
  ): Promise<Fee>;
}
```

In `src/services/http/index.ts`, add alongside the existing `fees:` block (matching whatever snake<->camel mapping helper this file already uses elsewhere — check for one near the top of the file, e.g. a `snakeToCamel`-equivalent, before writing this by hand):

```typescript
  fees: {
    list: async (childId) => { /* unchanged */ },
    pay: (feeId) => post<FeeInvoiceDTO>(`/fees/invoices/${feeId}/pay`, {}).then(toFee),
    createRazorpayOrder: async (feeId) => {
      const wire = await post<{ order_id: string; amount: number; currency: string; key_id: string }>(
        `/fees/invoices/${feeId}/razorpay/order`, {},
      );
      return { orderId: wire.order_id, amount: wire.amount, currency: wire.currency, keyId: wire.key_id };
    },
    verifyRazorpayPayment: (feeId, body) =>
      post<FeeInvoiceDTO>(`/fees/invoices/${feeId}/razorpay/verify`, {
        razorpay_order_id: body.razorpayOrderId,
        razorpay_payment_id: body.razorpayPaymentId,
        razorpay_signature: body.razorpaySignature,
      }).then(toFee),
  },
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm test -- razorpayFees`
Expected: PASS

- [ ] **Step 6: Run the whole sms-student test suite**

Run: `npm test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/services/types.ts src/services/http/index.ts src/services/http/razorpayFees.test.ts
git commit -m "feat(fees): Razorpay order/verify API client functions"
```

---

### Task 11: `sms-student` — WebView-based Razorpay Checkout component

**Files:**
- Modify: `package.json` (add `react-native-webview` dependency — no such dependency exists yet; check the currently-installed Expo SDK version's compatible `react-native-webview` version via `npx expo install react-native-webview` rather than hand-picking a version number)
- Create: `src/components/payments/RazorpayCheckoutModal.tsx`
- Test: `src/components/payments/RazorpayCheckoutModal.test.tsx`

**Interfaces:**
- Consumes: `RazorpayOrder` (Task 10).
- Produces: a `<RazorpayCheckoutModal order={order} visible={boolean} onSuccess={(result: { razorpayOrderId; razorpayPaymentId; razorpaySignature }) => void} onDismiss={() => void} schoolName={string} />` component — consumed by Task 12's `ParentFeesScreen` wiring.

- [ ] **Step 1: Install the dependency**

Run: `npx expo install react-native-webview` (from the `sms-student` repo root — this resolves the correct version for the installed Expo SDK automatically, avoiding a manual version mismatch)

- [ ] **Step 2: Write the failing test**

```tsx
import { render, fireEvent } from '@testing-library/react-native';
import { RazorpayCheckoutModal } from './RazorpayCheckoutModal';

const order = { orderId: 'order_x', amount: 480000, currency: 'INR', keyId: 'rzp_test_x' };

describe('RazorpayCheckoutModal', () => {
  it('renders a WebView pointed at a checkout page carrying the order details', () => {
    const { UNSAFE_getByType } = render(
      <RazorpayCheckoutModal order={order} visible schoolName="Green Valley School" onSuccess={jest.fn()} onDismiss={jest.fn()} />,
    );
    const WebView = require('react-native-webview').WebView;
    const webview = UNSAFE_getByType(WebView);
    expect(webview.props.source.html).toContain('rzp_test_x');
    expect(webview.props.source.html).toContain('order_x');
  });

  it('calls onSuccess with the parsed payment result when the page posts a success message', () => {
    const onSuccess = jest.fn();
    const { UNSAFE_getByType } = render(
      <RazorpayCheckoutModal order={order} visible schoolName="Green Valley School" onSuccess={onSuccess} onDismiss={jest.fn()} />,
    );
    const WebView = require('react-native-webview').WebView;
    const webview = UNSAFE_getByType(WebView);
    webview.props.onMessage({
      nativeEvent: {
        data: JSON.stringify({
          type: 'success',
          razorpay_order_id: 'order_x', razorpay_payment_id: 'pay_x', razorpay_signature: 'sig_x',
        }),
      },
    });
    expect(onSuccess).toHaveBeenCalledWith({ razorpayOrderId: 'order_x', razorpayPaymentId: 'pay_x', razorpaySignature: 'sig_x' });
  });

  it('calls onDismiss when the page posts a dismiss/failure message', () => {
    const onDismiss = jest.fn();
    const { UNSAFE_getByType } = render(
      <RazorpayCheckoutModal order={order} visible schoolName="Green Valley School" onSuccess={jest.fn()} onDismiss={onDismiss} />,
    );
    const WebView = require('react-native-webview').WebView;
    const webview = UNSAFE_getByType(WebView);
    webview.props.onMessage({ nativeEvent: { data: JSON.stringify({ type: 'dismiss' }) } });
    expect(onDismiss).toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npm test -- RazorpayCheckoutModal`
Expected: FAIL — component doesn't exist

- [ ] **Step 4: Write the component**

```tsx
import { Modal, SafeAreaView, StyleSheet } from 'react-native';
import { WebView, type WebViewMessageEvent } from 'react-native-webview';
import type { RazorpayOrder } from '@/services/types';

interface Props {
  order: RazorpayOrder;
  visible: boolean;
  schoolName: string;
  onSuccess: (result: { razorpayOrderId: string; razorpayPaymentId: string; razorpaySignature: string }) => void;
  onDismiss: () => void;
}

function checkoutHtml(order: RazorpayOrder, schoolName: string): string {
  return `<!DOCTYPE html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
  <script src="https://checkout.razorpay.com/v1/checkout.js"></script></head>
  <body style="margin:0">
  <script>
    var options = {
      key: '${order.keyId}',
      amount: '${order.amount}',
      currency: '${order.currency}',
      order_id: '${order.orderId}',
      name: '${schoolName.replace(/'/g, "\\'")}',
      description: 'Fee payment',
      handler: function (response) {
        window.ReactNativeWebView.postMessage(JSON.stringify({
          type: 'success',
          razorpay_order_id: response.razorpay_order_id,
          razorpay_payment_id: response.razorpay_payment_id,
          razorpay_signature: response.razorpay_signature,
        }));
      },
      modal: {
        ondismiss: function () {
          window.ReactNativeWebView.postMessage(JSON.stringify({ type: 'dismiss' }));
        },
      },
    };
    var rzp = new Razorpay(options);
    rzp.open();
  </script>
  </body></html>`;
}

export function RazorpayCheckoutModal({ order, visible, schoolName, onSuccess, onDismiss }: Props) {
  const handleMessage = (event: WebViewMessageEvent) => {
    const msg = JSON.parse(event.nativeEvent.data);
    if (msg.type === 'success') {
      onSuccess({
        razorpayOrderId: msg.razorpay_order_id,
        razorpayPaymentId: msg.razorpay_payment_id,
        razorpaySignature: msg.razorpay_signature,
      });
    } else if (msg.type === 'dismiss') {
      onDismiss();
    }
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onDismiss}>
      <SafeAreaView style={styles.safe}>
        <WebView
          originWhitelist={['*']}
          source={{ html: checkoutHtml(order, schoolName) }}
          onMessage={handleMessage}
        />
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({ safe: { flex: 1 } });
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm test -- RazorpayCheckoutModal`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Run the whole sms-student test suite**

Run: `npm test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add package.json package-lock.json src/components/payments/RazorpayCheckoutModal.tsx src/components/payments/RazorpayCheckoutModal.test.tsx
git commit -m "feat(fees): WebView-based Razorpay Checkout modal (no native module/prebuild needed)"
```

---

### Task 12: `sms-student` — wire `ParentFeesScreen` to the real payment flow

**Files:**
- Modify: `src/hooks/useFees.ts` (replace `usePayFee`'s stub-calling shape with the order-create/verify flow's mutations)
- Modify: `src/features/parent/screens/ParentFeesScreen.tsx` (open `RazorpayCheckoutModal` on "Pay now", call verify on success)
- Test: any existing test file for `ParentFeesScreen`/`useFees` (check `src/hooks/useFees.test.ts` and `src/features/parent/screens/ParentFeesScreen.test.tsx` for existing coverage of `usePayFee`/`handlePay` before editing — read fully first)

**Interfaces:**
- Consumes: `services.fees.createRazorpayOrder`/`verifyRazorpayPayment` (Task 10), `RazorpayCheckoutModal` (Task 11).
- Produces: nothing consumed by later tasks — this is the final leaf that makes the feature reachable end-to-end.

- [ ] **Step 1: Read existing test coverage before touching anything**

Run: `grep -rln "usePayFee\|handlePay" src/hooks/*.test.ts src/features/parent/screens/*.test.tsx` and read every matching file fully — these tests assert the *old* fake-stub behavior (`payFee.mutateAsync` succeeding instantly) and must be rewritten, not left passing against dead code.

- [ ] **Step 2: Write the failing test for the new hook shape**

Adapt to whatever the existing test file's rendering/mocking conventions are (React Query test wrapper, mocked `services` module, etc. — copy the existing file's setup exactly). Target behavior:

```typescript
// useFees.test.ts (adapt file structure to match what Step 1 found)
it('useCreateRazorpayOrder calls services.fees.createRazorpayOrder', async () => {
  const order = { orderId: 'order_x', amount: 480000, currency: 'INR', keyId: 'rzp_test_x' };
  (services.fees.createRazorpayOrder as jest.Mock).mockResolvedValue(order);
  const { result } = renderHook(() => useCreateRazorpayOrder(), { wrapper });
  const returned = await act(() => result.current.mutateAsync('fee-1'));
  expect(services.fees.createRazorpayOrder).toHaveBeenCalledWith('fee-1');
  expect(returned).toEqual(order);
});

it('useVerifyRazorpayPayment calls services.fees.verifyRazorpayPayment and invalidates fees', async () => {
  (services.fees.verifyRazorpayPayment as jest.Mock).mockResolvedValue({ id: 'fee-1', status: 'paid' });
  const { result } = renderHook(() => useVerifyRazorpayPayment('child-1'), { wrapper });
  await act(() => result.current.mutateAsync({
    feeId: 'fee-1', body: { razorpayOrderId: 'order_x', razorpayPaymentId: 'pay_x', razorpaySignature: 'sig' },
  }));
  expect(services.fees.verifyRazorpayPayment).toHaveBeenCalledWith('fee-1', {
    razorpayOrderId: 'order_x', razorpayPaymentId: 'pay_x', razorpaySignature: 'sig',
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npm test -- useFees`
Expected: FAIL — `useCreateRazorpayOrder`/`useVerifyRazorpayPayment` don't exist

- [ ] **Step 4: Update `useFees.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { services } from '@/services';
import { qk } from './keys';
import type { RazorpayOrder } from '@/services/types';

export const useFees = (childId: string) =>
  useQuery({
    queryKey: qk.fees(childId),
    queryFn: () => services.fees.list(childId),
    enabled: Boolean(childId),
  });

export function useCreateRazorpayOrder() {
  return useMutation({ mutationFn: (feeId: string): Promise<RazorpayOrder> => services.fees.createRazorpayOrder(feeId) });
}

export function useVerifyRazorpayPayment(childId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ feeId, body }: {
      feeId: string;
      body: { razorpayOrderId: string; razorpayPaymentId: string; razorpaySignature: string };
    }) => services.fees.verifyRazorpayPayment(feeId, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.fees(childId) }),
  });
}
```

Remove the old `usePayFee` export entirely (replaced by the two hooks above) — grep for any other caller of `usePayFee` first (`grep -rn "usePayFee" src/`) and confirm `ParentFeesScreen.tsx` (updated in Step 6) is the only one before deleting.

- [ ] **Step 5: Run test to verify it passes**

Run: `npm test -- useFees`
Expected: PASS

- [ ] **Step 6: Update `ParentFeesScreen.tsx`**

Replace the `usePayFee`/`handlePay` wiring:

```tsx
import { useState } from 'react';
// ... existing imports ...
import { useFees, useCreateRazorpayOrder, useVerifyRazorpayPayment } from '@/hooks/useFees';
import { RazorpayCheckoutModal } from '@/components/payments/RazorpayCheckoutModal';
import type { RazorpayOrder } from '@/services/types';

export function ParentFeesScreen() {
  const { childId } = useSelectedChild();
  const toast = useToast();
  const feesQ = useFees(childId);
  const createOrder = useCreateRazorpayOrder();
  const verifyPayment = useVerifyRazorpayPayment(childId);
  const childrenQ = useChildren();
  const child = childrenQ.data?.find((c) => c.id === childId);
  const [checkout, setCheckout] = useState<{ order: RazorpayOrder; feeId: string } | null>(null);

  // ... handleDownload unchanged ...

  const handlePay = async (fee: Fee) => {
    try {
      const order = await createOrder.mutateAsync(fee.id);
      setCheckout({ order, feeId: fee.id });
    } catch {
      toast('Could not start payment. Please try again.');
    }
  };

  const handleCheckoutSuccess = async (result: { razorpayOrderId: string; razorpayPaymentId: string; razorpaySignature: string }) => {
    if (!checkout) return;
    setCheckout(null);
    try {
      await verifyPayment.mutateAsync({ feeId: checkout.feeId, body: result });
      toast('Payment successful');
    } catch {
      toast('Payment could not be confirmed. If money was deducted, it will be reflected shortly.');
    }
  };

  // ... existing loading/error/empty states unchanged ...

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      {/* ... existing JSX, only the Pay now Button's onPress/disabled and the added modal below ... */}
      <Button variant="white" full onPress={() => { void handlePay(due); }} disabled={createOrder.isPending}>
        {createOrder.isPending ? 'Starting payment…' : 'Pay now'}
      </Button>
      {checkout && (
        <RazorpayCheckoutModal
          order={checkout.order}
          visible
          schoolName={child?.school ?? 'School'}
          onSuccess={(result) => { void handleCheckoutSuccess(result); }}
          onDismiss={() => setCheckout(null)}
        />
      )}
      {/* ... rest of existing JSX unchanged ... */}
    </SafeAreaView>
  );
}
```

Fold this into the existing file's actual JSX structure (the `due ? (...)` block) rather than restructuring the whole component — only the `Button`'s `onPress`/`disabled` props change, and the modal is added as a sibling near the end of the returned tree, outside the `ScrollView`.

- [ ] **Step 7: Run test to verify it passes**

Run: `npm test -- ParentFeesScreen`
Expected: PASS (rewrite the existing `handlePay`-asserting tests found in Step 1 to instead assert `createOrder`/modal-open/`verifyPayment` behavior, following the same render/mock conventions already in that file)

- [ ] **Step 8: Run the whole sms-student test suite**

Run: `npm test`
Expected: PASS, no regressions.

- [ ] **Step 9: Manually verify with the run skill before considering this done**

Since this is a user-facing mobile flow, use the `run` skill to actually launch `sms-student` (Expo) and click through: select a child with a due fee, tap "Pay now," confirm the WebView opens Razorpay Checkout with the right amount, and (using Razorpay's test-mode card) confirm success returns to the app and the fee shows as paid. A passing test suite proves the code compiles and the units behave correctly — it does not prove the checkout experience actually works end-to-end in the real app.

- [ ] **Step 10: Commit**

```bash
git add src/hooks/useFees.ts src/hooks/useFees.test.ts src/features/parent/screens/ParentFeesScreen.tsx src/features/parent/screens/ParentFeesScreen.test.tsx
git commit -m "feat(fees): wire ParentFeesScreen to real Razorpay Checkout flow"
```

---

## Post-plan note: `pay_link` (staff "Send pay link") is intentionally not implemented

Task 4 leaves `RazorpayOrderResponse.PayLink` always `null`. `sms-admin`'s existing "Send pay link" button (`finance.tsx`'s `sendPayLink`) already handles this gracefully today (`if (order.payLink) {...} else { toast.info('Pay link unavailable', ...) }`), so nothing breaks — staff simply always see "Pay link unavailable" until a follow-up spec adds Razorpay Payment Links API integration. Flag this to the user as a known, explicitly-scoped-out gap once this plan completes, in case they want it prioritized sooner.
