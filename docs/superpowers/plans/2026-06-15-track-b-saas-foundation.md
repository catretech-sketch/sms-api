# Track B: SaaS Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the multi-tenant platform usable as a SaaS — email/mobile OTP login (alongside existing password login), a wired tier→feature gating mechanism, a per-request billing-state access gate, and single + bulk user provisioning — so provisioned/imported people can actually sign in.

**Architecture:** Builds on the green Phase 0 + Phase 0.5 backend (Dapper + stored procs, FluentMigrator, JWT, RLS). New migrations `M0033` (schema: generalise `OtpCodes`, add `UsersTvp` type) and `M0034` (embedded procs). OTP orchestration lives in the auth endpoints (matching existing style); repositories stay pure data-access. Tenant tier+status is loaded once per request into a scoped `ITenantPlan` and enforced by a billing-state middleware; feature gating is an opt-in endpoint filter.

**Tech Stack:** .NET 10 Minimal APIs, Dapper, Microsoft.Data.SqlClient, FluentMigrator, xUnit + WebApplicationFactory + FluentAssertions, real dev SQL Server `DESKTOP-TJL4SG6` (throwaway DB per test run).

**Spec:** `docs/superpowers/specs/2026-06-15-track-b-saas-foundation-design.md`.

**Conventions:** Commands run from repo root `D:\SMS\sms-project\sms-backend` (Git Bash path `/d/SMS/sms-project/sms-backend`). Build: `dotnet build`. Tests: `dotnet test`. **Stop any running dev server first** (`dotnet run` holds the build lock): PowerShell `Get-NetTCPConnection -LocalPort 5162 -State Listen | %{ Stop-Process -Id $_.OwningProcess -Force }`. Every code task is TDD: failing test → run-fail → implement → run-pass → commit.

---

## File Structure

**New files:**
- `db/Sms.Migrations/M0033_Saas_Auth.cs` — schema: generalise `OtpCodes`, create `dbo.UsersTvp`.
- `db/Sms.Migrations/M0034_Procs_Saas.cs` — applies embedded `procs/saas/*.sql`.
- `db/Sms.Migrations/procs/saas/{User_GetByPhone,Otp_Insert,Otp_GetActive,Otp_Consume,User_Create,UserRole_Add,User_SetPassword,Tenant_GetTierAndStatus,Users_BulkCreate}.sql`
- `src/Sms.Shared.Kernel/Tenancy/ITenantPlan.cs`, `TenantPlan.cs`, `TenantPlanRepository.cs`
- `src/Sms.Shared.Kernel/Tenancy/BillingStateMiddleware.cs`
- `src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs`, `TierFeatures.cs`, `TierFeatureSet.cs`, `RequiresFeatureFilter.cs`
- `src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs`
- `src/Sms.Api/Endpoints/UserEndpoints.cs`
- Test files under `tests/Sms.Tests.Unit/...` and `tests/Sms.Tests.Integration/Saas/...`

**Modified files:**
- `src/Sms.Shared.Kernel/Auth/IOtpSender.cs`, `ConsoleOtpSender.cs` — channel-aware.
- `src/Sms.Shared.Kernel/Auth/AuthRepository.cs` — phone lookup, OTP, set-password.
- `src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs` — load plan.
- `src/Sms.Api/Endpoints/AuthEndpoints.cs` — OTP request/verify + set-password.
- `src/Sms.Api/Auth/LoginModels.cs` — new request DTOs.
- `src/Sms.Modules.Tenancy/ModuleEndpoints.cs` — seed admin user on client create.
- `src/Sms.Api/Program.cs` — DI + middleware order + endpoint mapping.
- `src/Sms.Api/Swagger/ApiAudienceMap.cs` — `/v1/users` → school-admin.

---

## Task 1: Generalise OtpCodes schema + channel-aware IOtpSender

**Files:**
- Create: `db/Sms.Migrations/M0033_Saas_Auth.cs`
- Modify: `src/Sms.Shared.Kernel/Auth/IOtpSender.cs`, `src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs`
- Test: `tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class ConsoleOtpSenderTests
{
    [Fact]
    public async Task Generates_a_six_digit_code_for_any_channel()
    {
        var sender = new ConsoleOtpSender();
        var code = await sender.SendAsync("user@x.com", "email");
        code.Should().MatchRegex("^[0-9]{6}$");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter ConsoleOtpSenderTests`
Expected: FAIL — `SendAsync` takes one argument / signature mismatch (compile error).

- [ ] **Step 3: Generalise the interface + implementation**

Replace `src/Sms.Shared.Kernel/Auth/IOtpSender.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public interface IOtpSender
{
    /// Sends an OTP to the identifier (email or phone) over the channel ("email"|"sms")
    /// and returns the plaintext code (caller hashes + stores it). Real delivery = Track C.
    Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default);
}
```

Replace `src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs`:
```csharp
using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

public sealed class ConsoleOtpSender : IOtpSender
{
    public Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Console.WriteLine($"[OTP/{channel}] {identifier} -> {code}"); // stub; real SMS/email = Track C
        return Task.FromResult(code);
    }
}
```

- [ ] **Step 4: Create the schema migration**

Create `db/Sms.Migrations/M0033_Saas_Auth.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(33, "SaaS auth: generalise OtpCodes to identifier+channel; UsersTvp type for bulk import")]
public sealed class M0033_Saas_Auth : Migration
{
    public override void Up()
    {
        Alter.Table("OtpCodes")
            .AddColumn("Identifier").AsString(256).Nullable()
            .AddColumn("Channel").AsString(10).Nullable();
        Alter.Column("Phone").OnTable("OtpCodes").AsString(32).Nullable();
        Create.Index("IX_OtpCodes_Identifier").OnTable("OtpCodes").OnColumn("Identifier").Ascending();

        Execute.Sql("CREATE TYPE dbo.UsersTvp AS TABLE " +
                    "(Email nvarchar(256) NULL, Phone nvarchar(32) NULL, Role nvarchar(64) NULL);");
    }

    public override void Down()
    {
        Execute.Sql("DROP TYPE IF EXISTS dbo.UsersTvp;");
        Delete.Index("IX_OtpCodes_Identifier").OnTable("OtpCodes");
        Delete.Column("Identifier").FromTable("OtpCodes");
        Delete.Column("Channel").FromTable("OtpCodes");
    }
}
```

- [ ] **Step 5: Run test + build to verify pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter ConsoleOtpSenderTests`
Expected: PASS (1 test). Then `dotnet build db/Sms.Migrations` → `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add db/Sms.Migrations/M0033_Saas_Auth.cs src/Sms.Shared.Kernel/Auth/IOtpSender.cs src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs
git commit -m "feat(saas): channel-aware IOtpSender + OtpCodes identifier/channel + UsersTvp type (M0033)"
```

---

## Task 2: SaaS stored procedures (M0034 + procs/saas/*.sql)

**Files:**
- Create: nine files in `db/Sms.Migrations/procs/saas/` + `db/Sms.Migrations/M0034_Procs_Saas.cs`
- Test: covered by integration tasks (real DB). Build-only here.

The proc folder name `saas` maps to embedded-resource namespace fragment `procs.saas.` (the csproj already embeds `procs/**/*.sql`).

- [ ] **Step 1: Write the proc SQL files**

Create `db/Sms.Migrations/procs/saas/User_GetByPhone.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.User_GetByPhone
    @Phone nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.Phone = @Phone
    ORDER BY u.CreatedAt;
END
```

Create `db/Sms.Migrations/procs/saas/Otp_Insert.sql` (replaces the phone-only version from M0003 via CREATE OR ALTER):
```sql
CREATE OR ALTER PROCEDURE dbo.Otp_Insert
    @Identifier nvarchar(256),
    @Channel nvarchar(10),
    @CodeHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.OtpCodes (Identifier, Channel, CodeHash, ExpiresAt)
    VALUES (@Identifier, @Channel, @CodeHash, @ExpiresAt);
END
```

Create `db/Sms.Migrations/procs/saas/Otp_GetActive.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Otp_GetActive
    @Identifier nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 o.Id, o.CodeHash
    FROM dbo.OtpCodes o
    WHERE o.Identifier = @Identifier AND o.ConsumedAt IS NULL AND o.ExpiresAt > SYSUTCDATETIME()
    ORDER BY o.CreatedAt DESC;
END
```

Create `db/Sms.Migrations/procs/saas/Otp_Consume.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Otp_Consume
    @Identifier nvarchar(256),
    @CodeHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OtpCodes SET ConsumedAt = SYSUTCDATETIME()
    WHERE Identifier = @Identifier AND CodeHash = @CodeHash AND ConsumedAt IS NULL;
END
```

Create `db/Sms.Migrations/procs/saas/User_Create.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.User_Create
    @TenantId uniqueidentifier,
    @Email nvarchar(256),
    @Phone nvarchar(32),
    @IsPlatform bit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status)
    VALUES (@Id, @TenantId, @Email, @Phone, ISNULL(@IsPlatform, 0), 'active');
    SELECT @Id AS Id;
END
```

Create `db/Sms.Migrations/procs/saas/UserRole_Add.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.UserRole_Add
    @UserId uniqueidentifier,
    @Role nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role = @Role)
        INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, @Role);
END
```

Create `db/Sms.Migrations/procs/saas/User_SetPassword.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.User_SetPassword
    @UserId uniqueidentifier,
    @PasswordHash nvarchar(512)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Users SET PasswordHash = @PasswordHash, Status = 'active' WHERE Id = @UserId;
END
```

Create `db/Sms.Migrations/procs/saas/Tenant_GetTierAndStatus.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Tenant_GetTierAndStatus
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.Tier, t.Status FROM dbo.Tenants t WHERE t.Id = @TenantId;
END
```

Create `db/Sms.Migrations/procs/saas/Users_BulkCreate.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Users_BulkCreate
    @TenantId uniqueidentifier,
    @Rows dbo.UsersTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @created int = 0, @skipped int = 0;

    -- Stage rows with a generated id; skip rows whose email/phone already exists in the tenant.
    DECLARE @New TABLE (Id uniqueidentifier, Email nvarchar(256), Phone nvarchar(32), Role nvarchar(64));
    INSERT @New (Id, Email, Phone, Role)
    SELECT NEWID(), r.Email, r.Phone, r.Role
    FROM @Rows r
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Users u
        WHERE u.TenantId = @TenantId
          AND ((r.Email IS NOT NULL AND u.Email = r.Email)
            OR (r.Phone IS NOT NULL AND u.Phone = r.Phone)));

    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status)
    SELECT Id, @TenantId, Email, Phone, 0, 'active' FROM @New;
    SET @created = @@ROWCOUNT;

    INSERT dbo.UserRoles (UserId, Role)
    SELECT Id, Role FROM @New WHERE Role IS NOT NULL;

    SELECT @created AS Created,
           (SELECT COUNT(*) FROM @Rows) - @created AS Skipped;
END
```

- [ ] **Step 2: Create the migration that applies the embedded procs**

Create `db/Sms.Migrations/M0034_Procs_Saas.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(34, "SaaS procs: OTP (identifier), phone lookup, user create/roles/set-password, bulk create, tenant tier/status")]
public sealed class M0034_Procs_Saas : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[]
        {
            "User_GetByPhone", "Otp_Consume", "User_Create", "UserRole_Add",
            "User_SetPassword", "Tenant_GetTierAndStatus", "Users_BulkCreate"
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add db/Sms.Migrations/procs/saas db/Sms.Migrations/M0034_Procs_Saas.cs
git commit -m "feat(saas): stored procs — OTP(identifier), phone lookup, user create/roles/password, bulk create, tenant tier/status (M0034)"
```

---

## Task 3: AuthRepository — phone lookup, OTP, set-password

**Files:**
- Modify: `src/Sms.Shared.Kernel/Auth/AuthRepository.cs`
- Test: covered by integration Task 4 (needs real DB + endpoints). Build-only here.

- [ ] **Step 1: Add the data-access methods**

Replace `src/Sms.Shared.Kernel/Auth/AuthRepository.cs`:
```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class AuthRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetByEmail", new { Email = email }, ct);

    public Task<UserRecord?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetByPhone", new { Phone = phone }, ct);

    public Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetById", new { Id = id }, ct);

    public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<string>("dbo.UserRoles_GetByUser", new { UserId = userId }, ct);

    public Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.User_SetPassword", new { UserId = userId, PasswordHash = passwordHash }, ct);

    public Task OtpInsertAsync(string identifier, string channel, string codeHash,
        DateTime expiresAt, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Otp_Insert",
            new { Identifier = identifier, Channel = channel, CodeHash = codeHash, ExpiresAt = expiresAt }, ct);

    /// Returns the active code's stored hash, or null when none is active.
    public async Task<string?> OtpActiveHashAsync(string identifier, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<OtpRow>("dbo.Otp_GetActive", new { Identifier = identifier }, ct);
        return rows.Count == 0 ? null : rows[0].CodeHash;
    }

    public Task OtpConsumeAsync(string identifier, string codeHash, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Otp_Consume", new { Identifier = identifier, CodeHash = codeHash }, ct);

    private sealed record OtpRow(Guid Id, string CodeHash);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Sms.Shared.Kernel`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth/AuthRepository.cs
git commit -m "feat(saas): AuthRepository — phone lookup, OTP insert/active/consume, set-password"
```

---

## Task 4: OTP login endpoints + set-password

**Files:**
- Modify: `src/Sms.Api/Auth/LoginModels.cs`, `src/Sms.Api/Endpoints/AuthEndpoints.cs`
- Test: `tests/Sms.Tests.Integration/Saas/OtpLoginTests.cs`

- [ ] **Step 1: Add request DTOs**

Append to `src/Sms.Api/Auth/LoginModels.cs`:
```csharp
public sealed record OtpRequest(string Identifier);
public sealed record OtpVerifyRequest(string Identifier, string Code);
public sealed record SetPasswordRequest(string Password);
```

- [ ] **Step 2: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Saas/OtpLoginTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class OtpLoginTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    // The real code is random and only printed to the console, so after requesting an OTP we overwrite
    // its stored hash with the hash of a known code ("123456"). This keeps the test deterministic
    // without scraping stdout; production never exposes the code.
    [Fact]
    public async Task Otp_request_then_verify_issues_tokens_for_known_email()
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var email = $"otp{Guid.NewGuid():N}@x.com";
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("INSERT dbo.Users (Id, Email, IsPlatform) VALUES (NEWID(),@e,0)",
                new { e = email });

        await using var app = App();
        var client = app.CreateClient();

        // Request always returns 200 (no account-existence leak).
        (await client.PostAsJsonAsync("/v1/auth/otp/request", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/v1/auth/otp/request", new { identifier = "nobody@x.com" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Read the issued code from the DB (sha256 hash stored), brute the 6-digit space is avoided by
        // recomputing the hash for each candidate is impractical; instead overwrite with a known code.
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "UPDATE dbo.OtpCodes SET CodeHash = @h WHERE Identifier = @id",
                new { id = email, h = Sha256Hex("123456") });

        var verify = await client.PostAsJsonAsync("/v1/auth/otp/verify",
            new { identifier = email, code = "123456" });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("access_token").GetString()
            .Should().NotBeNullOrEmpty();

        // A wrong code → 401.
        (await client.PostAsJsonAsync("/v1/auth/otp/verify", new { identifier = email, code = "000000" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter OtpLoginTests`
Expected: FAIL — `/v1/auth/otp/request` returns 404 (endpoint not mapped).

- [ ] **Step 4: Implement the endpoints**

In `src/Sms.Api/Endpoints/AuthEndpoints.cs`, add these three endpoints inside `MapAuth`, immediately after the existing `/refresh` mapping (before `/me`). They use the existing private `Sha256` helper already defined at the bottom of the file:
```csharp
        g.MapPost("/otp/request", async (OtpRequest req, AuthRepository users, IOtpSender otp,
            ITenantContext tenant) =>
        {
            // Lookups run as a system (platform) session — Users is RLS-protected and the caller is anon.
            tenant.Set(null, null, isPlatform: true);
            var isEmail = req.Identifier.Contains('@');
            var channel = isEmail ? "email" : "sms";
            var user = isEmail
                ? await users.GetByEmailAsync(req.Identifier)
                : await users.GetByPhoneAsync(req.Identifier);
            if (user is not null)
            {
                var code = await otp.SendAsync(req.Identifier, channel);
                await users.OtpInsertAsync(req.Identifier, channel, Sha256(code),
                    DateTime.UtcNow.AddMinutes(10));
            }
            // Always 200 — never leak whether the account exists.
            return Results.Ok(new DataEnvelope<object>(new { sent = true }));
        });

        g.MapPost("/otp/verify", async (OtpVerifyRequest req, AuthRepository users,
            IJwtTokenService jwt, IRefreshTokenStore tokens, ITenantContext tenant) =>
        {
            tenant.Set(null, null, isPlatform: true);
            var activeHash = await users.OtpActiveHashAsync(req.Identifier);
            if (activeHash is null || activeHash != Sha256(req.Code))
                return Results.Json(ErrorEnvelope.From(new("invalid_code", "code invalid or expired")),
                    statusCode: 401);
            await users.OtpConsumeAsync(req.Identifier, activeHash);

            var user = req.Identifier.Contains('@')
                ? await users.GetByEmailAsync(req.Identifier)
                : await users.GetByPhoneAsync(req.Identifier);
            if (user is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_code", "user not found")),
                    statusCode: 401);

            var roles = await users.GetRolesAsync(user.Id);
            var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30));
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, refresh)));
        });

        g.MapPost("/set-password", async (SetPasswordRequest req, AuthRepository users,
            IPasswordHasher hasher, ITenantContext tenant) =>
        {
            if (tenant.UserId is not { } uid) return Results.Unauthorized();
            await users.SetPasswordAsync(uid, hasher.Hash(req.Password));
            return Results.NoContent();
        }).RequireAuthorization();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter OtpLoginTests`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Api/Auth/LoginModels.cs src/Sms.Api/Endpoints/AuthEndpoints.cs tests/Sms.Tests.Integration/Saas/OtpLoginTests.cs
git commit -m "feat(saas): OTP login (request/verify, non-leaking) + authenticated set-password"
```

---

## Task 5: Tenant plan accessor + per-request load + billing-state gate

**Files:**
- Create: `src/Sms.Shared.Kernel/Tenancy/ITenantPlan.cs`, `TenantPlan.cs`, `TenantPlanRepository.cs`, `BillingStateMiddleware.cs`
- Modify: `src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs`, `src/Sms.Api/Program.cs`
- Test: `tests/Sms.Tests.Unit/Tenancy/BillingStateMiddlewareTests.cs`, `tests/Sms.Tests.Integration/Saas/BillingGateTests.cs`

- [ ] **Step 1: Create the plan accessor + repository**

Create `src/Sms.Shared.Kernel/Tenancy/ITenantPlan.cs`:
```csharp
namespace Sms.Shared.Kernel.Tenancy;

public interface ITenantPlan
{
    Guid? TenantId { get; }
    string Tier { get; }
    string Status { get; }
    void Set(Guid? tenantId, string tier, string status);
}
```

Create `src/Sms.Shared.Kernel/Tenancy/TenantPlan.cs`:
```csharp
namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantPlan : ITenantPlan
{
    public Guid? TenantId { get; private set; }
    public string Tier { get; private set; } = "";
    public string Status { get; private set; } = "";

    public void Set(Guid? tenantId, string tier, string status)
    {
        TenantId = tenantId;
        Tier = tier ?? "";
        Status = status ?? "";
    }
}
```

Create `src/Sms.Shared.Kernel/Tenancy/TenantPlanRepository.cs`:
```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantPlanRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public sealed record TierStatus(string? Tier, string? Status);

    public async Task<TierStatus?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<TierStatus>("dbo.Tenant_GetTierAndStatus",
            new { TenantId = tenantId }, ct);
        return rows.Count == 0 ? null : rows[0];
    }
}
```

- [ ] **Step 2: Load the plan in TenantResolutionMiddleware**

Replace `src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, ITenantPlan plan,
        TenantPlanRepository planRepo)
    {
        var user = http.User;
        var isPlatform = user.FindFirst("is_platform")?.Value == "1";
        Guid? userId = Guid.TryParse(user.FindFirst("sub")?.Value, out var uid) ? uid : null;
        Guid? tokenTenant = Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tt) ? tt : null;

        Guid? headerTenant = Guid.TryParse(http.Request.Headers["X-Tenant-Id"].ToString(), out var ht) ? ht : null;

        if (!isPlatform && tokenTenant is { } a && headerTenant is { } b && a != b)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tid = headerTenant ?? tokenTenant;
        tenant.Set(tid, userId, isPlatform);

        // Load tier+status once per request for tenant callers (Tenants is not RLS-scoped).
        if (!isPlatform && tid is { } t)
        {
            var ts = await planRepo.GetAsync(t);
            plan.Set(t, ts?.Tier ?? "", ts?.Status ?? "");
        }

        await next(http);
    }
}
```

- [ ] **Step 3: Write the failing unit test for the billing gate decision**

Create `tests/Sms.Tests.Unit/Tenancy/BillingStateMiddlewareTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class BillingStateMiddlewareTests
{
    [Theory]
    [InlineData("active", "POST", false, 0)]
    [InlineData("trial", "POST", false, 0)]
    [InlineData("past_due", "GET", false, 0)]
    [InlineData("past_due", "POST", false, 402)]
    [InlineData("suspended", "GET", false, 403)]
    [InlineData("suspended", "POST", false, 403)]
    [InlineData("suspended", "POST", true, 0)]   // platform exempt
    public void Decides_block_code(string status, string method, bool isPlatform, int expected)
    {
        BillingStateMiddleware.BlockCode(status, method, isPlatform, path: "/v1/students")
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("suspended", "POST", "/v1/auth/otp/request")] // auth always allowed
    [InlineData("past_due", "POST", "/v1/auth/login")]
    public void Auth_paths_are_never_blocked(string status, string method, string path) =>
        BillingStateMiddleware.BlockCode(status, method, isPlatform: false, path).Should().Be(0);
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter BillingStateMiddlewareTests`
Expected: FAIL — `BillingStateMiddleware` does not exist.

- [ ] **Step 5: Implement the billing middleware**

Create `src/Sms.Shared.Kernel/Tenancy/BillingStateMiddleware.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class BillingStateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, ITenantPlan plan)
    {
        var code = BlockCode(plan.Status, http.Request.Method, tenant.IsPlatform,
            http.Request.Path.Value ?? "");
        if (code == 0) { await next(http); return; }

        http.Response.StatusCode = code;
        http.Response.ContentType = "application/json";
        var err = code == 402
            ? new Error("payment_required", "Account past due — writes are disabled until payment.")
            : new Error("tenant_suspended", "Account suspended. Contact support.");
        await http.Response.WriteAsJsonAsync(ErrorEnvelope.From(err));
    }

    /// 0 = allow; otherwise the HTTP status to return. Pure for testing.
    public static int BlockCode(string status, string method, bool isPlatform, string path)
    {
        if (isPlatform) return 0;
        if (path.StartsWith("/v1/auth/", StringComparison.OrdinalIgnoreCase)) return 0;

        if (string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase))
            return StatusCodes.Status403Forbidden;

        if (string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase))
            return IsWrite(method) ? StatusCodes.Status402PaymentRequired : 0;

        return 0; // active / trial / unknown
    }

    private static bool IsWrite(string method) =>
        method is not ("GET" or "HEAD" or "OPTIONS");
}
```

- [ ] **Step 6: Run unit test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter BillingStateMiddlewareTests`
Expected: PASS (9 cases).

- [ ] **Step 7: Wire DI + middleware order in Program.cs**

In `src/Sms.Api/Program.cs`, register the scoped services next to the other `AddScoped` calls (after `AddScoped<ITenantContext, TenantContext>()`):
```csharp
builder.Services.AddScoped<ITenantPlan, TenantPlan>();
builder.Services.AddScoped<TenantPlanRepository>();
```
Then in the middleware pipeline, insert `BillingStateMiddleware` immediately after the tenant-resolution middleware:
```csharp
app.UseMiddleware<TenantResolutionMiddleware>(); // after auth: needs ClaimsPrincipal
app.UseMiddleware<BillingStateMiddleware>();       // after tenant resolution: needs ITenantPlan
```

- [ ] **Step 8: Write the failing integration test for the gate**

Create `tests/Sms.Tests.Integration/Saas/BillingGateTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Time;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class BillingGateTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private async Task<Guid> SeedTenant(string status)
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var id = Guid.NewGuid();
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync(
            "INSERT dbo.Tenants (Id, Name, Slug, Status, Tier) VALUES (@id,@n,@s,@st,'gold')",
            new { id, n = "T", s = $"t{id:N}", st = status });
        return id;
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.admin"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Past_due_allows_reads_blocks_writes()
    {
        var tid = await SeedTenant("past_due");
        await using var app = App();
        var client = ClientFor(app, tid);

        (await client.GetAsync("/v1/students")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/v1/students", new { name = "x", admission_no = "A1" }))
            .StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Suspended_blocks_everything_except_auth()
    {
        var tid = await SeedTenant("suspended");
        await using var app = App();
        var client = ClientFor(app, tid);

        (await client.GetAsync("/v1/students")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // auth still reachable (422 = validation, NOT blocked by the gate)
        (await client.PostAsJsonAsync("/v1/auth/login", new { }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
```

- [ ] **Step 9: Run integration test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter BillingGateTests`
Expected: PASS (2 tests).

- [ ] **Step 10: Commit**

```bash
git add src/Sms.Shared.Kernel/Tenancy tests/Sms.Tests.Unit/Tenancy/BillingStateMiddlewareTests.cs tests/Sms.Tests.Integration/Saas/BillingGateTests.cs src/Sms.Api/Program.cs
git commit -m "feat(saas): per-request tenant plan load + billing-state gate (past_due=402, suspended=403, platform exempt)"
```

---

## Task 6: Tier→feature gating (catalog, map, feature set, endpoint filter)

**Files:**
- Create: `src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs`, `TierFeatures.cs`, `TierFeatureSet.cs`, `RequiresFeatureFilter.cs`
- Modify: `src/Sms.Api/Program.cs` (DI)
- Test: `tests/Sms.Tests.Unit/Authz/TierFeatureSetTests.cs`, `tests/Sms.Tests.Unit/Authz/RequiresFeatureFilterTests.cs`

- [ ] **Step 1: Write the failing unit tests**

Create `tests/Sms.Tests.Unit/Authz/TierFeatureSetTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class TierFeatureSetTests
{
    private static ITenantFeatureSet ForTier(string tier)
    {
        var plan = new TenantPlan();
        plan.Set(Guid.NewGuid(), tier, "active");
        return new TierFeatureSet(plan);
    }

    [Theory]
    [InlineData("silver")]
    [InlineData("gold")]
    [InlineData("platinum")]
    [InlineData("")] // unknown tier
    public void All_tiers_grant_every_catalog_feature(string tier)
    {
        var set = ForTier(tier);
        foreach (var f in FeatureCatalog.All)
            set.Has(f).Should().BeTrue($"{tier} grants {f} (all-level policy)");
    }

    [Fact]
    public void Unknown_feature_key_is_not_granted()
    {
        ForTier("gold").Has("does.not.exist").Should().BeFalse();
    }
}
```

Create `tests/Sms.Tests.Unit/Authz/RequiresFeatureFilterTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class RequiresFeatureFilterTests
{
    private sealed class StubSet(bool has) : ITenantFeatureSet
    {
        public bool Has(string feature) => has;
    }

    [Fact]
    public void Locked_returns_403_feature_locked()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(false), "transport.gps", isPlatform: false)
            .Should().Be(403);
    }

    [Fact]
    public void Allowed_returns_zero()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(true), "transport.gps", isPlatform: false)
            .Should().Be(0);
    }

    [Fact]
    public void Platform_bypasses()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(false), "transport.gps", isPlatform: true)
            .Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter "TierFeatureSetTests|RequiresFeatureFilterTests"`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement the catalog, map, and feature set**

Create `src/Sms.Shared.Kernel/Authz/FeatureCatalog.cs`:
```csharp
namespace Sms.Shared.Kernel.Authz;

/// Every known gateable feature key. RequiresFeature("x") must use a key listed here.
public static class FeatureCatalog
{
    public const string TransportGps = "transport.gps";
    public const string ExamsDatesheet = "exams.datesheet";
    public const string ReportsCsv = "reports.csv";
    public const string AnalyticsAdvanced = "analytics.advanced";
    public const string CommsTargeted = "comms.announcements.targeted";

    public static readonly string[] All =
        [TransportGps, ExamsDatesheet, ReportsCsv, AnalyticsAdvanced, CommsTargeted];
}
```

Create `src/Sms.Shared.Kernel/Authz/TierFeatures.cs`:
```csharp
namespace Sms.Shared.Kernel.Authz;

/// tier -> granted feature keys. Decision (2026-06-15, "all level"): ALL tiers grant the full
/// catalog — nothing is locked yet. To restrict a tier later, return a subset here; no endpoint
/// changes needed because RequiresFeature already enforces this map.
public static class TierFeatures
{
    public static IReadOnlyCollection<string> For(string tier) => FeatureCatalog.All;
}
```

Create `src/Sms.Shared.Kernel/Authz/TierFeatureSet.cs`:
```csharp
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Shared.Kernel.Authz;

public sealed class TierFeatureSet(ITenantPlan plan) : ITenantFeatureSet
{
    public bool Has(string feature) => TierFeatures.For(plan.Tier).Contains(feature);
}
```

- [ ] **Step 4: Implement the endpoint filter**

Create `src/Sms.Shared.Kernel/Authz/RequiresFeatureFilter.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Shared.Kernel.Authz;

/// Endpoint filter enforcing a RequiresFeatureAttribute on the endpoint. Opt in with
/// `.RequiresFeature("transport.gps")` (the route-builder helper below).
public sealed class RequiresFeatureFilter(string feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var features = ctx.HttpContext.RequestServices.GetService(typeof(ITenantFeatureSet)) as ITenantFeatureSet;
        var tenant = ctx.HttpContext.RequestServices.GetService(typeof(ITenantContext)) as ITenantContext;
        var code = Evaluate(features, feature, tenant?.IsPlatform ?? false);
        if (code == 0) return await next(ctx);
        return Results.Json(ErrorEnvelope.From(new Error("feature_locked",
            $"This feature ({feature}) is not available on your plan.")), statusCode: code);
    }

    /// 0 = allow; 403 = locked. Pure for testing.
    public static int Evaluate(ITenantFeatureSet? features, string feature, bool isPlatform)
    {
        if (isPlatform) return 0;
        return features is not null && features.Has(feature) ? 0 : StatusCodes.Status403Forbidden;
    }
}

public static class RequiresFeatureExtensions
{
    public static TBuilder RequiresFeature<TBuilder>(this TBuilder builder, string feature)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequiresFeatureFilter(feature));
        return builder;
    }
}
```

- [ ] **Step 5: Register the feature set in DI**

In `src/Sms.Api/Program.cs`, after the `AddScoped<TenantPlanRepository>()` line from Task 5:
```csharp
builder.Services.AddScoped<Sms.Shared.Kernel.Authz.ITenantFeatureSet, Sms.Shared.Kernel.Authz.TierFeatureSet>();
```

- [ ] **Step 6: Run tests + build to verify pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter "TierFeatureSetTests|RequiresFeatureFilterTests"`
Expected: PASS. Then `dotnet build` → `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Shared.Kernel/Authz tests/Sms.Tests.Unit/Authz src/Sms.Api/Program.cs
git commit -m "feat(saas): tier->feature gating wired (all tiers grant all features) + RequiresFeature endpoint filter"
```

---

## Task 7: User provisioning — admin seed, single invite, bulk import

**Files:**
- Create: `src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs`, `src/Sms.Api/Endpoints/UserEndpoints.cs`
- Modify: `src/Sms.Modules.Tenancy/ModuleEndpoints.cs`, `src/Sms.Api/Program.cs`, `src/Sms.Api/Swagger/ApiAudienceMap.cs`
- Test: `tests/Sms.Tests.Integration/Saas/ProvisioningTests.cs`

- [ ] **Step 1: Create the provisioning repository**

Create `src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs`:
```csharp
using System.Data;
using Dapper;
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed record ImportRow(string? Email, string? Phone, string? Role);
public sealed record ImportResult(int Created, int Skipped);

public sealed class UserProvisioningRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    /// Creates one login-ready user (Status='active', no password — logs in via OTP) and its roles.
    public async Task<Guid> CreateUserAsync(Guid? tenantId, string? email, string? phone,
        bool isPlatform, IEnumerable<string> roles, CancellationToken ct = default)
    {
        var id = await QuerySingleProcAsync<Guid>("dbo.User_Create",
            new { TenantId = tenantId, Email = email, Phone = phone, IsPlatform = isPlatform }, ct);
        foreach (var role in roles)
            await ExecuteProcAsync("dbo.UserRole_Add", new { UserId = id, Role = role }, ct);
        return id;
    }

    /// Bulk-creates login users + roles in one TVP round-trip; skips duplicate email/phone in-tenant.
    public async Task<ImportResult> BulkCreateAsync(Guid tenantId, IReadOnlyList<ImportRow> rows,
        CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("Role", typeof(string));
        foreach (var r in rows)
            table.Rows.Add((object?)r.Email ?? DBNull.Value, (object?)r.Phone ?? DBNull.Value,
                (object?)r.Role ?? DBNull.Value);

        var p = new DynamicParameters();
        p.Add("@TenantId", tenantId);
        p.Add("@Rows", table.AsTableValuedParameter("dbo.UsersTvp"));

        var result = await QuerySingleProcAsync<ImportResult>("dbo.Users_BulkCreate", p, ct);
        return result ?? new ImportResult(0, rows.Count);
    }
}
```

Note: `QuerySingleProcAsync<Guid>` maps the `SELECT @Id AS Id` single-column row.

- [ ] **Step 2: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Saas/ProvisioningTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Time;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class ProvisioningTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient AdminClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.admin"], isPlatform: false);
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }

    private async Task<Guid> SeedActiveTenant()
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var id = Guid.NewGuid();
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync("INSERT dbo.Tenants (Id, Name, Slug, Status, Tier) VALUES (@id,'T',@s,'active','gold')",
            new { id, s = $"t{id:N}" });
        return id;
    }

    [Fact]
    public async Task Invite_user_then_that_user_can_otp_login_with_role()
    {
        var tid = await SeedActiveTenant();
        await using var app = App();
        var admin = AdminClient(app, tid);
        var email = $"teacher{Guid.NewGuid():N}@x.com";

        (await admin.PostAsJsonAsync("/v1/users",
            new { email, roles = new[] { "school.teacher" } })).StatusCode.Should().Be(HttpStatusCode.Created);

        // OTP login as the invited user (overwrite the code hash with a known value).
        var anon = app.CreateClient();
        (await anon.PostAsJsonAsync("/v1/auth/otp/request", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("UPDATE dbo.OtpCodes SET CodeHash=@h WHERE Identifier=@id",
                new { id = email, h = Sha256Hex("123456") });

        var verify = await anon.PostAsJsonAsync("/v1/auth/otp/verify", new { identifier = email, code = "123456" });
        using var doc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("data").GetProperty("access_token").GetString();
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Bulk_import_creates_users_and_skips_duplicates()
    {
        var tid = await SeedActiveTenant();
        await using var app = App();
        var admin = AdminClient(app, tid);
        var e1 = $"a{Guid.NewGuid():N}@x.com";
        var e2 = $"b{Guid.NewGuid():N}@x.com";

        var res = await admin.PostAsJsonAsync("/v1/users/import", new
        {
            rows = new[]
            {
                new { email = e1, phone = (string?)null, role = "school.teacher" },
                new { email = e2, phone = (string?)null, role = "student.parent" },
                new { email = e1, phone = (string?)null, role = "school.teacher" }, // duplicate
            }
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("created").GetInt32().Should().Be(2);
        data.GetProperty("skipped").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Non_admin_cannot_invite()
    {
        var tid = await SeedActiveTenant();
        await using var app = App();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tid, ["school.teacher"], isPlatform: false);
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);

        (await c.PostAsJsonAsync("/v1/users", new { email = "x@y.com", roles = new[] { "school.teacher" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string Sha256Hex(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter ProvisioningTests`
Expected: FAIL — `/v1/users` returns 404 (not mapped).

- [ ] **Step 4: Create the user endpoints**

Create `src/Sms.Api/Endpoints/UserEndpoints.cs`:
```csharp
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Endpoints;

public sealed record InviteUserRequest(string? Email, string? Phone, string[] Roles);
public sealed record ImportUsersRequest(ImportRowDto[] Rows);
public sealed record ImportRowDto(string? Email, string? Phone, string? Role);

public static class UserEndpoints
{
    private static readonly HashSet<string> AssignableRoles = new(
        Policies.All.Where(r => r != Policies.PlatformOnly), StringComparer.OrdinalIgnoreCase);

    public static void MapUsers(this WebApplication app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapPost("/users", async (InviteUserRequest req, UserProvisioningRepository repo,
            ITenantContext tenant, HttpContext http) =>
        {
            if (!IsSchoolAdmin(http)) return Forbidden("school admin only");
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            if (req.Email is null && req.Phone is null)
                return Invalid("email or phone required");
            if (req.Roles.Length == 0 || req.Roles.Any(r => !AssignableRoles.Contains(r)))
                return Invalid("invalid role(s)");

            var id = await repo.CreateUserAsync(tid, req.Email, req.Phone, false, req.Roles);
            return Results.Json(new DataEnvelope<object>(new { id }), statusCode: 201);
        });

        g.MapPost("/users/import", async (ImportUsersRequest req, UserProvisioningRepository repo,
            ITenantContext tenant, HttpContext http) =>
        {
            if (!IsSchoolAdmin(http)) return Forbidden("school admin only");
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");

            var rows = req.Rows
                .Where(r => (r.Email is not null || r.Phone is not null)
                            && (r.Role is null || AssignableRoles.Contains(r.Role)))
                .Select(r => new ImportRow(r.Email, r.Phone, r.Role))
                .ToList();
            var result = await repo.BulkCreateAsync(tid, rows);
            return Results.Ok(new DataEnvelope<ImportResult>(result));
        });
    }

    private static bool IsSchoolAdmin(HttpContext http) =>
        http.User.FindAll("role").Any(c => c.Value == Policies.SchoolAdmin);

    private static IResult Forbidden(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", m)), statusCode: 403);
    private static IResult Invalid(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("invalid_request", m)), statusCode: 422);
}
```

- [ ] **Step 5: Seed the school admin on client creation**

In `src/Sms.Modules.Tenancy/ModuleEndpoints.cs`, replace the `MapPost("/clients", ...)` handler so it also provisions the admin user when `AdminEmail`/`AdminPhone` is supplied. The `UserProvisioningRepository` is injected:
```csharp
        g.MapPost("/clients", async (CreateClientRequest req, ClientRepository repo,
            Sms.Shared.Kernel.Auth.UserProvisioningRepository users) =>
        {
            var row = await repo.CreateAsync(req);
            if (row is not null && (req.AdminEmail is not null || req.AdminPhone is not null))
                await users.CreateUserAsync(row.Id, req.AdminEmail, req.AdminPhone, false,
                    new[] { Sms.Shared.Kernel.Authz.Policies.SchoolAdmin });
            return Results.Json(new DataEnvelope<ClientResponse>(row!.ToResponse()), statusCode: 201);
        });
```

- [ ] **Step 6: Register repo + map endpoints + Swagger audience**

In `src/Sms.Api/Program.cs`: register the repo next to `AddScoped<AuthRepository>()`:
```csharp
builder.Services.AddScoped<UserProvisioningRepository>();
```
Map the endpoints next to `app.MapAuth()`:
```csharp
app.MapUsers();
```
In `src/Sms.Api/Swagger/ApiAudienceMap.cs`, add a rule for `/v1/users` (school admin) — place it among the other `("v1/...", [...])` rules:
```csharp
        ("v1/users",         [SchoolAdmin]),
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter ProvisioningTests`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs src/Sms.Api/Endpoints/UserEndpoints.cs src/Sms.Modules.Tenancy/ModuleEndpoints.cs src/Sms.Api/Program.cs src/Sms.Api/Swagger/ApiAudienceMap.cs tests/Sms.Tests.Integration/Saas/ProvisioningTests.cs
git commit -m "feat(saas): user provisioning — admin seed on client create + POST /v1/users invite + /v1/users/import (TVP)"
```

---

## Task 8: Full-suite verification

**Files:** none (verification only).

- [ ] **Step 1: Stop any running dev server**

PowerShell: `Get-NetTCPConnection -LocalPort 5162 -State Listen -ErrorAction SilentlyContinue | %{ Stop-Process -Id $_.OwningProcess -Force }`

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 3: Run the entire test suite**

Run: `dotnet test`
Expected: all unit + integration tests PASS (the prior 82 plus the new SaaS tests), 0 failures.

- [ ] **Step 4: Smoke-check the new surface in Swagger (optional)**

Start the API (`dotnet run --project src/Sms.Api --launch-profile http`), open
`http://localhost:5162/swagger`, confirm `school-admin` doc lists `/v1/users` and `/v1/users/import`
and every app doc lists `/v1/auth/otp/request`, `/v1/auth/otp/verify`, `/v1/auth/set-password`. Stop the
server afterward.

- [ ] **Step 5: Final commit (if any verification fixups were needed)**

```bash
git add -A
git commit -m "test(saas): Track B full-suite green"
```

---

## Notes for the implementer

- **RLS + provisioning sessions.** `dbo.Users` has an RLS filter+block predicate keyed on
  `SESSION_CONTEXT('TenantId')`/`IsPlatform`. The auth endpoints set a platform session
  (`tenant.Set(null, null, isPlatform: true)`) before lookups, exactly like the existing `/login`.
  `POST /v1/users` runs under the school admin's own tenant session, so inserting a user with that same
  `TenantId` satisfies the block predicate. `Tenants` is **not** RLS-scoped, so plan/status reads work
  under any session.
- **OTP code in tests.** Tests overwrite `OtpCodes.CodeHash` with `Sha256Hex("123456")` rather than
  scraping the console, keeping them deterministic. Production code never exposes the code.
- **`QuerySingleProcAsync<Guid>`** maps `SELECT @Id AS Id` (single scalar column) — Dapper handles the
  one-column projection.
- **Middleware order matters:** `TenantResolution` must run before `BillingState` (the gate reads
  `ITenantPlan`, which resolution fills), and both run after `UseAuthentication`.
```
