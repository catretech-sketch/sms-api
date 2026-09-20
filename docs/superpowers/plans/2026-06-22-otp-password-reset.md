# OTP-gated Password Create / Reset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two dedicated `/v1/auth` endpoints — `password/forgot` (send OTP to a registered email/phone) and `password/reset` (verify OTP, set a new password, no auto-login) — so all client apps have a clean create/forgot-password contract.

**Architecture:** Minimal-API endpoints in `Sms.Api`, reusing the existing OTP infrastructure (`dbo.OtpCodes` via `Otp_Insert`/`Otp_GetActive`/`Otp_Consume`) and `dbo.User_SetPassword`. No DB migration and no new repository methods. The OTP-send block in the existing `/otp/request` is extracted into a private helper shared by `/password/forgot`.

**Tech Stack:** C# / ASP.NET Core minimal APIs, Dapper, SQL Server, xUnit + FluentAssertions integration tests (`WebApplicationFactory<Program>` against a `SqlServerFixture`).

## Global Constraints

- Endpoints live under `app.MapGroup("/v1/auth").RequireRateLimiting("auth")` in `src/Sms.Api/Endpoints/AuthEndpoints.cs`.
- Lookups against `dbo.Users` MUST run as a system/platform session: `tenant.Set(null, null, isPlatform: true)` before any `users.GetBy*` call (the table is RLS-protected; caller is anonymous).
- OTP codes are stored/compared as `Sha256(code)` using the existing private `Sha256` helper in `AuthEndpoints` (uppercase hex, `Convert.ToHexString`).
- Error responses use `Results.Json(ErrorEnvelope.From(new("<code>", "<message>")), statusCode: <n>)`; success bodies use `DataEnvelope<T>`.
- Password minimum length: **8 characters**. Reset returns **`204 No Content`** with no token body.
- Keep the existing `404 not_registered` enumeration behavior (consistency with `/otp/request`).

---

### Task 1: Request models + `/password/forgot` endpoint (with shared OTP-send helper)

**Files:**
- Modify: `src/Sms.Api/Auth/LoginModels.cs` (add two records)
- Modify: `src/Sms.Api/Endpoints/AuthEndpoints.cs` (extract helper; refactor `/otp/request`; add `/password/forgot`)
- Test: `tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs` (create)

**Interfaces:**
- Consumes: `AuthRepository.GetByEmailAsync`, `.GetByPhoneAsync`, `.OtpInsertAsync`; `IOtpSender.SendAsync(identifier, channel)`; `ITenantContext.Set`; existing private `Sha256(string)`.
- Produces: `ForgotPasswordRequest(string Identifier)`; `ResetPasswordRequest(string Identifier, string Code, string Password)` (used in Task 2); private `SendOtpToRegisteredAsync(string identifier, AuthRepository users, IOtpSender otp, ITenantContext tenant) -> Task<IResult>`; route `POST /v1/auth/password/forgot`.

- [ ] **Step 1: Add the request models**

In `src/Sms.Api/Auth/LoginModels.cs`, append after the existing `SetPasswordRequest` record:

```csharp
public sealed record ForgotPasswordRequest(string Identifier);
public sealed record ResetPasswordRequest(string Identifier, string Code, string Password);
```

- [ ] **Step 2: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs`:

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
public class PasswordResetTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    private async Task<string> InsertUserAsync(SqlConnectionFactory factory, string email)
    {
        await using var c = await factory.OpenAsync();
        await c.ExecuteAsync("INSERT dbo.Users (Id, Email, IsPlatform) VALUES (NEWID(),@e,0)",
            new { e = email });
        return email;
    }

    private static SqlConnectionFactory Factory(SqlServerFixture fx)
    {
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        return new SqlConnectionFactory(fx.ConnectionString, ctx);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public async Task Forgot_returns_404_for_unregistered_and_200_for_registered()
    {
        var factory = Factory(fx);
        var email = await InsertUserAsync(factory, $"reset{Guid.NewGuid():N}@x.com");

        await using var app = App();
        var client = app.CreateClient();

        var unknown = await client.PostAsJsonAsync("/v1/auth/password/forgot",
            new { identifier = "nobody-reset@x.com" });
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var err = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
            err.RootElement.GetProperty("error").GetProperty("message").GetString()
                .Should().Be("Email is not registered.");

        (await client.PostAsJsonAsync("/v1/auth/password/forgot", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var c = await factory.OpenAsync();
        var count = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.OtpCodes WHERE Identifier = @id", new { id = email });
        count.Should().BeGreaterThan(0);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~PasswordResetTests"`
Expected: FAIL — `/v1/auth/password/forgot` returns 404 for the *registered* email too (route not mapped yet), so the `200` assertion fails (or the route 404s for all).

- [ ] **Step 4: Extract the shared OTP-send helper and wire `/otp/request` to it**

In `src/Sms.Api/Endpoints/AuthEndpoints.cs`, replace the body of the existing `g.MapPost("/otp/request", ...)` so it delegates to a new helper. The handler becomes:

```csharp
        g.MapPost("/otp/request", (OtpRequest req, AuthRepository users, IOtpSender otp,
            ITenantContext tenant) => SendOtpToRegisteredAsync(req.Identifier, users, otp, tenant));
```

Then add this private helper alongside the existing `Sha256` method:

```csharp
    private static async Task<IResult> SendOtpToRegisteredAsync(
        string identifier, AuthRepository users, IOtpSender otp, ITenantContext tenant)
    {
        // Lookups run as a system (platform) session — Users is RLS-protected and the caller is anon.
        tenant.Set(null, null, isPlatform: true);
        var isEmail = identifier.Contains('@');
        var channel = isEmail ? "email" : "sms";
        var user = isEmail
            ? await users.GetByEmailAsync(identifier)
            : await users.GetByPhoneAsync(identifier);

        // Only registered identifiers get an OTP; unregistered ones are told so (no OTP generated).
        // This intentionally reveals account existence (enumeration) for clearer login UX.
        if (user is null)
            return Results.Json(ErrorEnvelope.From(new("not_registered",
                isEmail ? "Email is not registered." : "Phone is not registered.")), statusCode: 404);

        var code = await otp.SendAsync(identifier, channel);
        await users.OtpInsertAsync(identifier, channel, Sha256(code), DateTime.UtcNow.AddMinutes(10));
        return Results.Ok(new DataEnvelope<object>(new { sent = true }));
    }
```

- [ ] **Step 5: Add the `/password/forgot` endpoint**

In the same file, after the `/otp/verify` mapping, add:

```csharp
        g.MapPost("/password/forgot", (ForgotPasswordRequest req, AuthRepository users,
            IOtpSender otp, ITenantContext tenant) =>
            SendOtpToRegisteredAsync(req.Identifier, users, otp, tenant));
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~PasswordResetTests"`
Expected: PASS (1 test).

- [ ] **Step 7: Run the existing OTP test to confirm the refactor is non-behavioral**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~OtpLoginTests"`
Expected: PASS (existing test still green).

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Api/Auth/LoginModels.cs src/Sms.Api/Endpoints/AuthEndpoints.cs tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs
git commit -m "feat(auth): POST /v1/auth/password/forgot (OTP to registered identifier)"
```

---

### Task 2: `/password/reset` endpoint

**Files:**
- Modify: `src/Sms.Api/Endpoints/AuthEndpoints.cs` (add `/password/reset`)
- Test: `tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs` (add cases)

**Interfaces:**
- Consumes: `ResetPasswordRequest` (Task 1); `AuthRepository.OtpActiveHashAsync`, `.OtpConsumeAsync`, `.GetByEmailAsync`, `.GetByPhoneAsync`, `.SetPasswordAsync`; `IPasswordHasher.Hash(string)`; private `Sha256(string)`.
- Produces: route `POST /v1/auth/password/reset` → `422 weak_password` | `401 invalid_code` | `204 No Content`.

- [ ] **Step 1: Write the failing tests**

Append these methods inside the `PasswordResetTests` class in `tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs`:

```csharp
    [Fact]
    public async Task Reset_with_valid_code_sets_password_and_does_not_return_tokens()
    {
        var factory = Factory(fx);
        var email = await InsertUserAsync(factory, $"reset{Guid.NewGuid():N}@x.com");

        await using var app = App();
        var client = app.CreateClient();

        (await client.PostAsJsonAsync("/v1/auth/password/forgot", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Overwrite the random stored hash with a known code so the test is deterministic.
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("UPDATE dbo.OtpCodes SET CodeHash = @h WHERE Identifier = @id",
                new { id = email, h = Sha256Hex("123456") });

        var reset = await client.PostAsJsonAsync("/v1/auth/password/reset",
            new { identifier = email, code = "123456", password = "newSecret1" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await reset.Content.ReadAsStringAsync()).Should().NotContain("access_token");

        // The new password works at /login.
        var login = await client.PostAsJsonAsync("/v1/auth/login",
            new { email, password = "newSecret1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_with_wrong_code_returns_401()
    {
        var factory = Factory(fx);
        var email = await InsertUserAsync(factory, $"reset{Guid.NewGuid():N}@x.com");

        await using var app = App();
        var client = app.CreateClient();

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { identifier = email });
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("UPDATE dbo.OtpCodes SET CodeHash = @h WHERE Identifier = @id",
                new { id = email, h = Sha256Hex("123456") });

        (await client.PostAsJsonAsync("/v1/auth/password/reset",
            new { identifier = email, code = "000000", password = "newSecret1" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_with_short_password_returns_422()
    {
        var factory = Factory(fx);
        var email = await InsertUserAsync(factory, $"reset{Guid.NewGuid():N}@x.com");

        await using var app = App();
        var client = app.CreateClient();

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { identifier = email });
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync("UPDATE dbo.OtpCodes SET CodeHash = @h WHERE Identifier = @id",
                new { id = email, h = Sha256Hex("123456") });

        (await client.PostAsJsonAsync("/v1/auth/password/reset",
            new { identifier = email, code = "123456", password = "short" }))
            .StatusCode.Should().Be((HttpStatusCode)422);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~PasswordResetTests"`
Expected: FAIL — the three new tests get `404` (route not mapped); `Reset_with_short_password` would not yet see `422`.

- [ ] **Step 3: Add the `/password/reset` endpoint**

In `src/Sms.Api/Endpoints/AuthEndpoints.cs`, after the `/password/forgot` mapping, add:

```csharp
        g.MapPost("/password/reset", async (ResetPasswordRequest req, AuthRepository users,
            IPasswordHasher hasher, ITenantContext tenant) =>
        {
            if (req.Password is null || req.Password.Length < 8)
                return Results.Json(ErrorEnvelope.From(new("weak_password",
                    "password must be at least 8 characters")), statusCode: 422);

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

            await users.SetPasswordAsync(user.Id, hasher.Hash(req.Password));
            return Results.NoContent();
        });
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~PasswordResetTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Api/Endpoints/AuthEndpoints.cs tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs
git commit -m "feat(auth): POST /v1/auth/password/reset (verify OTP, set password, no auto-login)"
```

---

## Self-Review

**Spec coverage:**
- `password/forgot` (send OTP, 404 unregistered / 200 registered) → Task 1. ✓
- `password/reset` (422 weak / 401 invalid / consume OTP / set password / 204 no tokens) → Task 2. ✓
- Shared OTP-send helper / `/otp/request` non-behavioral refactor → Task 1 Steps 4 & 7. ✓
- Min-8 password policy → Task 2 Step 3. ✓
- No DB migration / no new repo methods → confirmed; all repo methods (`OtpActiveHashAsync`, `OtpConsumeAsync`, `SetPasswordAsync`, `GetBy*`) already exist. ✓
- Enumeration retained → helper preserves the `404 not_registered` message. ✓
- Tests mirror `OtpLoginTests` (insert user, overwrite `OtpCodes.CodeHash` with `Sha256Hex("123456")`). ✓

**Placeholder scan:** none — every step shows full code/commands.

**Type consistency:** `ForgotPasswordRequest`/`ResetPasswordRequest` defined in Task 1 and consumed in Tasks 1/2 with matching shapes; `SendOtpToRegisteredAsync` signature consistent across `/otp/request` and `/password/forgot`; `Sha256` is the existing private helper; `ErrorEnvelope.From(new(code, message))` and `DataEnvelope<object>` match existing `AuthEndpoints` usage.
