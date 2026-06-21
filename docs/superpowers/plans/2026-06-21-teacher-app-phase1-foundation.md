# Teacher+Principal App — Phase 1: Authz & Pagination Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared authorization and pagination primitives the rest of the Teacher+Principal app
work depends on, and enforce server-side role gating on the principal-only endpoints.

**Architecture:** Add a pure cursor codec and named authorization policies to `Sms.Shared.Kernel` /
`Program.cs`, then apply the principal policy to the principal-only routes. Pure helpers are unit-tested;
authz is integration-tested with minted JWTs.

**Tech Stack:** .NET 10 minimal APIs, ASP.NET authorization policies, JWT (HS256, `role` claims), Dapper,
xUnit + FluentAssertions, FluentMigrator (unchanged this phase).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-21-teacher-principal-app-complete-design.md`.
- Wire format is **snake_case**; responses wrap in `DataEnvelope<T>` or `CursorPage<T>`; errors use
  `ErrorEnvelope.From(new Error(code, message))` (type `Sms.Shared.Kernel.Results.Error`).
- Canonical role strings are the `Sms.Shared.Kernel.Authz.Policies` constants:
  `school.admin`, `school.principal`, `school.teacher`, `staff`, `student.parent`. Never use bare
  `"teacher"`.
- JWT already emits `role` claims and `Program.cs` sets `RoleClaimType = "role"`, so `RequireRole` works.
- Pagination request type already exists: `PageRequest(int Limit = 50, string? Cursor = null)` with
  `SafeLimit` (clamps to 1..200, else 50). `CursorPage<T>(IReadOnlyList<T> Data, string? NextCursor)`
  already exists. Do **not** redefine these.
- Tests follow the existing integration pattern (mint a token via `IJwtTokenService.IssueAccess`, seed a
  tenant, set RLS context).

---

## File Structure

- `src/Sms.Shared.Kernel/Http/Cursor.cs` — **new**. Pure opaque-cursor encode/decode.
- `tests/Sms.Tests.Unit/Http/CursorTests.cs` — **new**. Unit tests for the codec.
- `src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs` — **new**. `AddSmsAuthorization()` extension
  registering the named policies (keeps `Program.cs` lean and the matrix in one place).
- `src/Sms.Api/Program.cs` — **modify** (~line 89-90). Call `AddSmsAuthorization()` instead of the inline
  single-policy `AddAuthorization`.
- `src/Sms.Modules.Comms/CommsModule.cs` — **modify** (~line 138). `POST /announcements` requires
  principal policy.
- `src/Sms.Modules.Staffing/StaffingModule.cs` — **modify** (~lines 88, 91). `GET /approvals` +
  `PATCH /approvals/{id}` require principal policy.
- `tests/Sms.Tests.Integration/Authz/PrincipalPolicyTests.cs` — **new**. 403/200 matrix for the gated
  routes.
- `tests/Sms.Tests.Integration/Comms/CommsTests.cs`, `.../Staffing/LeaveApprovalsTests.cs`,
  `.../Attendance/GeofenceCheckinTests.cs` — **modify**. Replace bare `"teacher"` role tokens with
  `"school.teacher"` (and `"school.principal"` where a route is now gated).

---

### Task 1: Opaque cursor codec

**Files:**
- Create: `src/Sms.Shared.Kernel/Http/Cursor.cs`
- Test: `tests/Sms.Tests.Unit/Http/CursorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class Sms.Shared.Kernel.Http.Cursor` with
  `string Encode(string rawSortKey)` and `string? Decode(string? cursor)` (returns `null` for null/empty
  or malformed input — callers treat `null` as "start from the beginning").

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Http;
using Xunit;

namespace Sms.Tests.Unit.Http;

public class CursorTests
{
    [Fact]
    public void Encode_then_Decode_roundtrips()
    {
        var key = "Sharma|3f2504e0-4f89-41d3-9a0c-0305e82c3301";
        Cursor.Decode(Cursor.Encode(key)).Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-valid-base64!!")]
    public void Decode_returns_null_for_empty_or_malformed(string? input)
    {
        Cursor.Decode(input).Should().BeNull();
    }

    [Fact]
    public void Encode_is_url_safe_base64_without_padding_newlines()
    {
        var c = Cursor.Encode("a|b");
        c.Should().NotContain("\n").And.NotContain("+").And.NotContain("/").And.NotContain("=");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter FullyQualifiedName~CursorTests`
Expected: FAIL — `Cursor` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text;

namespace Sms.Shared.Kernel.Http;

/// Opaque keyset-pagination cursor. Encodes the last row's sort key as URL-safe base64 (no padding)
/// so clients treat it as a blob. Decode returns null for null/empty/malformed input (= start over).
public static class Cursor
{
    public static string Encode(string rawSortKey)
    {
        var b = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawSortKey));
        return b.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        var b = cursor.Replace('-', '+').Replace('_', '/');
        switch (b.Length % 4) { case 2: b += "=="; break; case 3: b += "="; break; }
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b)); }
        catch (FormatException) { return null; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter FullyQualifiedName~CursorTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Http/Cursor.cs tests/Sms.Tests.Unit/Http/CursorTests.cs
git commit -m "feat(http): opaque keyset cursor codec for pagination"
```

---

### Task 2: Register named authorization policies

**Files:**
- Create: `src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs`
- Modify: `src/Sms.Api/Program.cs:89-90`

**Interfaces:**
- Consumes: `Policies` constants (`Sms.Shared.Kernel.Authz.Policies`).
- Produces: `IServiceCollection AddSmsAuthorization(this IServiceCollection)` registering these policy
  names — used by `.RequireAuthorization("<name>")` in later tasks:
  - `"platform"` — `is_platform = 1` claim (unchanged behavior, moved here).
  - `Policies.Principal` (`"school.principal"`) — role `school.principal` **or** `school.admin`.
  - `"teacher.app"` — role `school.teacher`, `school.principal`, **or** `school.admin`.
  - `Policies.SchoolAdmin` (`"school.admin"`) — role `school.admin`.

- [ ] **Step 1: Write the implementation**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Sms.Shared.Kernel.Authz;

public static class AuthorizationPolicies
{
    /// Single place that maps policy names -> required roles, mirroring the frontend permission matrix.
    public const string TeacherApp = "teacher.app"; // teacher OR principal OR school admin

    public static IServiceCollection AddSmsAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy("platform", p => p.RequireClaim("is_platform", "1"))
            .AddPolicy(Policies.SchoolAdmin, p => p.RequireRole(Policies.SchoolAdmin))
            .AddPolicy(Policies.Principal, p => p.RequireRole(Policies.Principal, Policies.SchoolAdmin))
            .AddPolicy(TeacherApp, p => p.RequireRole(Policies.Teacher, Policies.Principal, Policies.SchoolAdmin))
            .Services;
}
```

- [ ] **Step 2: Wire it into Program.cs**

Replace the existing block at `src/Sms.Api/Program.cs:89-90`:

```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("platform", p => p.RequireClaim("is_platform", "1")));
```

with:

```csharp
builder.Services.AddSmsAuthorization();
```

Add `using Sms.Shared.Kernel.Authz;` to the usings at the top of `Program.cs` if not already present
(it is — `Policies` is in that namespace; confirm the `using` line exists).

- [ ] **Step 3: Build to verify it compiles and the platform policy still resolves**

Run: `dotnet build src/Sms.Api`
Expected: Build succeeded. (The existing `platform`-gated tenancy tests still pass because the policy
name and behavior are unchanged.)

- [ ] **Step 4: Run the existing platform-gated tests to confirm no regression**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~Catre`
Expected: PASS (unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Authz/AuthorizationPolicies.cs src/Sms.Api/Program.cs
git commit -m "feat(authz): named role policies (teacher.app, principal, school.admin)"
```

---

### Task 3: Enforce the principal policy on principal-only endpoints

**Files:**
- Modify: `src/Sms.Modules.Comms/CommsModule.cs:138` (`POST /announcements`)
- Modify: `src/Sms.Modules.Staffing/StaffingModule.cs:88,91` (`GET /approvals`, `PATCH /approvals/{id}`)
- Create: `tests/Sms.Tests.Integration/Authz/PrincipalPolicyTests.cs`
- Modify: `tests/Sms.Tests.Integration/Staffing/LeaveApprovalsTests.cs` (bare `"teacher"` → canonical)

**Interfaces:**
- Consumes: policy `Policies.Principal` from Task 2; `IJwtTokenService.IssueAccess(Guid userId,
  Guid? tenantId, IEnumerable<string> roles, bool isPlatform)`.
- Produces: principal-or-admin gating on the three routes (403 for other roles).

- [ ] **Step 1: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Integration.Authz;

public class PrincipalPolicyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public PrincipalPolicyTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Teacher_role_is_forbidden_on_approvals()
    {
        var (client, _) = await _f.NewTenantClientAsync(roles: new[] { Policies.Teacher });
        (await client.GetAsync("/v1/approvals")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Principal_role_can_read_approvals()
    {
        var (client, _) = await _f.NewTenantClientAsync(roles: new[] { Policies.Principal });
        (await client.GetAsync("/v1/approvals")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Teacher_role_is_forbidden_on_announcement_broadcast()
    {
        var (client, _) = await _f.NewTenantClientAsync(roles: new[] { Policies.Teacher });
        var resp = await client.PostAsJsonAsync("/v1/announcements",
            new { title = "x", body = "y", type = "info" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

> **Note on the test fixture:** this plan assumes a test helper that mints a tenant + authed `HttpClient`
> for given roles. The existing integration tests already construct this inline (see
> `LeaveApprovalsTests` / `CommsTests`: build `WebApplicationFactory`, call `jwt.IssueAccess(userId,
> tenantId, roles, false)`, set the `Authorization` + `X-Tenant-Id` headers). If no shared
> `ApiFactory.NewTenantClientAsync` helper exists yet, inline that same construction here using the
> canonical role strings instead of adding a new abstraction.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~PrincipalPolicyTests`
Expected: FAIL — routes currently return 200 for a teacher token (no role enforcement yet).

- [ ] **Step 3: Gate the approvals routes**

In `src/Sms.Modules.Staffing/StaffingModule.cs`, add `.RequireAuthorization(Policies.Principal)` to the
two approvals routes (add `using Sms.Shared.Kernel.Authz;` at the top):

```csharp
        g.MapGet("/approvals", async (LeaveRepository repo, string? status) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<LeaveResponse>>(
                await repo.ListByStatusAsync(status ?? "pending"))))
            .RequireAuthorization(Policies.Principal);

        g.MapPatch("/approvals/{id:guid}", async (Guid id, DecideLeaveRequest req, LeaveRepository repo, ITenantContext tenant) =>
            { /* unchanged body */ })
            .RequireAuthorization(Policies.Principal);
```

(Keep each existing handler body exactly as-is; only chain `.RequireAuthorization(Policies.Principal)`.)

- [ ] **Step 4: Gate the announcement broadcast**

In `src/Sms.Modules.Comms/CommsModule.cs`, chain the policy onto `POST /announcements` (add
`using Sms.Shared.Kernel.Authz;`):

```csharp
        g.MapPost("/announcements", async (CreateAnnouncementRequest req, CommsRepository repo, HttpContext http, ITenantContext tenant) =>
            { /* unchanged body */ })
            .RequireAuthorization(Policies.Principal);
```

(`GET /announcements` stays open to the group's default auth — all roles read announcements.)

- [ ] **Step 5: Fix the now-failing legacy test**

`tests/Sms.Tests.Integration/Staffing/LeaveApprovalsTests.cs:29` mints a bare `"teacher"` token and calls
approvals — it will now 403. That test exercises the *approval flow*, so it must act as a principal.
Change its token roles from `["teacher"]` to `[Policies.Principal]` (add
`using Sms.Shared.Kernel.Authz;`). Do **not** weaken the assertions.

- [ ] **Step 6: Run the new + affected tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~PrincipalPolicyTests|FullyQualifiedName~LeaveApprovals"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Modules.Staffing/StaffingModule.cs src/Sms.Modules.Comms/CommsModule.cs tests/Sms.Tests.Integration/Authz/PrincipalPolicyTests.cs tests/Sms.Tests.Integration/Staffing/LeaveApprovalsTests.cs
git commit -m "feat(authz): gate approvals + announcement broadcast to principal/admin"
```

---

### Task 4: Sweep remaining bare-role test tokens to canonical strings

**Files:**
- Modify: `tests/Sms.Tests.Integration/Comms/CommsTests.cs:29`
- Modify: `tests/Sms.Tests.Integration/Attendance/GeofenceCheckinTests.cs:31`

**Interfaces:**
- Consumes: nothing new.
- Produces: a codebase with no bare `"teacher"` role literals (so future authz tasks are not tripped by
  stale tokens).

- [ ] **Step 1: Find every bare-role literal**

Run: `git grep -n '"teacher"' tests/`
Expected: the two files above (the `LeaveApprovals` one was already fixed in Task 3). Note: matches inside
request bodies like `role = "teacher"` for a *chat contact* are domain data, **not** auth roles — leave
those. Only change `jwt.IssueAccess(..., ["teacher"], ...)` token roles.

- [ ] **Step 2: Replace the token roles**

In `CommsTests.cs:29` and `GeofenceCheckinTests.cs:31`, change the `IssueAccess` roles argument from
`["teacher"]` to `[Sms.Shared.Kernel.Authz.Policies.Teacher]` (add the `using` or fully-qualify).
These routes (`/v1/threads`, `/v1/me/attendance/*`) are not role-gated yet, so behavior is unchanged —
this is purely de-linting ahead of later phases.

- [ ] **Step 3: Run the affected tests**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~CommsTests|FullyQualifiedName~GeofenceCheckin"`
Expected: PASS (unchanged behavior).

- [ ] **Step 4: Commit**

```bash
git add tests/Sms.Tests.Integration/Comms/CommsTests.cs tests/Sms.Tests.Integration/Attendance/GeofenceCheckinTests.cs
git commit -m "test: use canonical school.teacher role string in token minting"
```

---

## Self-Review

- **Spec coverage (this phase only):** §7 role-based authz — policies registered (Task 2), principal-only
  enforcement (Task 3), role-string standardization (Tasks 3-4). §8 pagination — cursor codec primitive
  (Task 1); *applying* paging to `/students` and `/threads` is deferred to the endpoint phases (Phase 2),
  where the repos are modified, because keyset paging is written per-query. §9 validation and §5/§6
  feature endpoints are explicitly later phases.
- **Deferred-but-tracked:** broad per-route role enforcement on *shared* routes must mirror the
  `ApiAudienceMap` audience→role matrix (e.g. `/v1/students` serves Student app too) — each endpoint
  declares its policy as it is built/modified in Phase 2+, not blanket-applied here.
- **Placeholder scan:** none — every step has real code or a real command.
- **Type consistency:** `Policies.Principal`/`.Teacher`/`.SchoolAdmin`, `AuthorizationPolicies.TeacherApp`,
  `Cursor.Encode/Decode`, `IJwtTokenService.IssueAccess(...)`, `DataEnvelope<T>`, `LeaveResponse`,
  `DecideLeaveRequest` all match the existing source.

## Next phases (separate plan files, written when this one is reviewed)
- **Phase 2** — reuse-data endpoints (`classes/{id}/students` + paging, exam-papers PATCH/DELETE,
  check-in history/summary, dashboard/stats) + principal dashboards + Swagger mapping; each endpoint
  attaches its `ApiAudienceMap`-matching policy and validation.
- **Phase 3** — new-data features (timetable, calendar, library) + assignments↔homework reconciliation;
  migrations M0035+, RLS, dev seeds.
- **Phase 4** — bus-duty teacher view; Transport reuse + migrations.
- **Phase 5** — Swagger/test sweep; full integration run.
