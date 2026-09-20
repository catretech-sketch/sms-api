# Transport Live-Tracking Authorization & Broadcast Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a parent see only their child's bus, a duty teacher see only their assigned bus, and Principal/CRM continue seeing every bus in their tenant — all via real-time SignalR pushes — while never trusting a client-supplied bus/trip/tenant id.

**Architecture:** Extend the existing `TransportFleetHub`/`TransportFleetBroadcaster` (no new hub) with authorized per-bus groups (`bus:{busId}`), gated by a new `ITransportAuthorizationResolver` that resolves every decision server-side from the caller's JWT claims plus existing DB relationships (`BusAssignments`, `StudentBusAssignments`, `Trips`). A new lightweight background sweep detects and broadcasts "gone offline" since no ping will ever announce silence on its own.

**Tech Stack:** ASP.NET Core, SignalR, Dapper (raw SQL, no ORM), SQL Server with row-level security, xUnit + FluentAssertions (no mocking framework — DB-touching code is tested via `SqlServerFixture` real-database integration tests; only pure in-memory logic gets plain unit tests).

**Spec:** `docs/superpowers/specs/2026-09-04-transport-live-tracking-authorization-design.md`

## Global Constraints

- Never trust a client-supplied `busId`, `tripId`, `schoolId`, or `tenantId` as proof of access — every authorization decision is re-derived server-side from the authenticated caller's identity (JWT claims) and DB relationships.
- Groups are **bus-keyed** (`bus:{busId}`), not trip-keyed — a bus outlives any single trip.
- Reuse the existing `TransportFleetHub`/`TransportFleetBroadcaster` — do not add a new hub or a new client SignalR connection.
- No breaking changes to `TripController`'s existing REST contracts or `dbo.TripPings` ingestion shape — new repository methods are purely additive.
- Offline threshold: **60 seconds** of ping silence. Sweep interval: **20 seconds** (within the spec's 15-30s range).
- Broadcast payload for `position_update` is a full snapshot (lat/lng, speed, heading, status, last-update time, ETA) — never lat/lng alone.
- Trip lifecycle is pushed as explicit `trip_started`/`trip_ended` events, not inferred from silence.
- This codebase has **no mocking framework**. Any code that touches the database is tested via `SqlServerFixture` (real SQL Server, migrations run automatically, `[Collection("sql")]`) — never faked. Only pure, DB-free logic gets a plain xUnit unit test.
- `RoleClaimType` is configured to `"role"` globally, so `ClaimsPrincipal.IsInRole(...)` works correctly against the JWT's `"role"` claims — use it instead of manual `FindFirst("role")` string comparison for role checks in new code.
- `Policies.Teacher` and `Policies.Driver` are role-name constants only — they are **not** registered ASP.NET Core authorization policies (only `Policies.SchoolAdmin`, `Policies.Principal`, `AuthorizationPolicies.TeacherApp`, and `Policies.StudentOrParent` are registered via `AddAuthorizationBuilder()`). Do not write `[Authorize(Policy = Policies.Teacher)]` or `[Authorize(Policy = Policies.Driver)]` anywhere — they will throw at runtime.

---

### Task 1: Repository primitives for authorization checks

**Files:**
- Modify: `src/Sms.Modules.Transport/StudentBusModule.cs` — add `HasChildOnBusAsync`.
- Modify: `src/Sms.Modules.Transport/BusModule.cs` — add `IsDutyTeacherForBusAsync`.
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` — add `GetActiveDriverOrConductorRoleByBusAsync` and `GetBusIdAsync` to `TripRepository`.
- Test: `tests/Sms.Tests.Integration/Transport/TransportAuthorizationRepositoryTests.cs` (new)

**Interfaces:**
- Consumes: `BaseRepository.QueryInlineAsync<T>(string sql, object? args, CancellationToken ct)` (existing, protected, inherited).
- Produces (all consumed by Task 3's resolver, and `GetBusIdAsync` also by Task 6):
  - `StudentBusRepository.HasChildOnBusAsync(string admissionNo, Guid busId, CancellationToken ct = default) -> Task<bool>`
  - `BusRepository.IsDutyTeacherForBusAsync(Guid teacherUserId, Guid busId, CancellationToken ct = default) -> Task<bool>`
  - `TripRepository.GetActiveDriverOrConductorRoleByBusAsync(Guid busId, Guid userId, CancellationToken ct = default) -> Task<string?>` (returns `"driver"`, `"conductor"`, or `null`)
  - `TripRepository.GetBusIdAsync(Guid tripId, CancellationToken ct = default) -> Task<Guid?>`

- [ ] **Step 1: Confirm `StudentBusRepository.BusExistsAsync` already exists**

Run: `grep -n "BusExistsAsync" src/Sms.Modules.Transport/StudentBusModule.cs`

Expected: one match, a public method with a signature close to
`Task<bool> BusExistsAsync(Guid busId, CancellationToken ct = default)`. This
method is RLS-scoped (queries `dbo.Buses` under the caller's tenant session
context, so a bus belonging to another tenant returns `false`). Task 3 reuses
it as-is for the Principal/CRM tenant-membership check — if its actual
signature differs from the above, note the real signature now; Task 3 must
call it exactly as it exists.

- [ ] **Step 2: Add `HasChildOnBusAsync` to `StudentBusRepository`**

In `src/Sms.Modules.Transport/StudentBusModule.cs`, add this method to the
`StudentBusRepository` class, next to `ChildrenBusByAdmissionAsync`:

```csharp
/// True if a student with this admission number (RLS-scoped to the caller's
/// tenant) is currently assigned to this bus. Used to authorize a parent's
/// live-tracking subscription to their own child's bus only.
public async Task<bool> HasChildOnBusAsync(string admissionNo, Guid busId, CancellationToken ct = default)
{
    var rows = await QueryInlineAsync<int>(
        @"SELECT COUNT(1) FROM dbo.Students s
          JOIN dbo.StudentBusAssignments sba ON sba.StudentId = s.Id
          WHERE s.AdmissionNo = @admissionNo AND sba.BusId = @busId",
        new { admissionNo, busId }, ct);
    return rows.FirstOrDefault() > 0;
}
```

- [ ] **Step 3: Add `IsDutyTeacherForBusAsync` to `BusRepository`**

In `src/Sms.Modules.Transport/BusModule.cs`, add this method to the
`BusRepository` class, next to `GetAssignedAsync`:

```csharp
/// True if this teacher (RLS-scoped to the caller's tenant) is the assigned
/// duty teacher for this bus. Used to authorize a teacher's live-tracking
/// subscription to their assigned bus only — teaching a student who rides
/// the bus does NOT grant access on its own.
public async Task<bool> IsDutyTeacherForBusAsync(Guid teacherUserId, Guid busId, CancellationToken ct = default)
{
    var rows = await QueryInlineAsync<int>(
        "SELECT COUNT(1) FROM dbo.BusAssignments WHERE TeacherUserId = @teacherUserId AND BusId = @busId",
        new { teacherUserId, busId }, ct);
    return rows.FirstOrDefault() > 0;
}
```

- [ ] **Step 4: Add `GetActiveDriverOrConductorRoleByBusAsync` and `GetBusIdAsync` to `TripRepository`**

In `src/Sms.Modules.Transport/TransportModule.cs`, add this private record and
these two methods to the `TripRepository` class, next to
`GetParticipantRoleAsync`:

```csharp
private sealed record ActiveTripParticipantsRow(Guid? DriverId, Guid? ConductorId);

/// Returns "driver"/"conductor" if the caller is a participant of this bus's
/// currently-live trip, else null. Bus-keyed (not trip-keyed) so a caller can
/// check "am I driving/conducting this bus right now" without already
/// knowing today's TripId.
public async Task<string?> GetActiveDriverOrConductorRoleByBusAsync(Guid busId, Guid userId, CancellationToken ct = default)
{
    var row = (await QueryInlineAsync<ActiveTripParticipantsRow>(
        "SELECT TOP 1 DriverId, ConductorId FROM dbo.Trips WHERE BusId = @busId AND Status = 'live' ORDER BY StartedAt DESC",
        new { busId }, ct)).FirstOrDefault();
    if (row is null) return null;
    if (row.DriverId == userId) return "driver";
    if (row.ConductorId == userId) return "conductor";
    return null;
}

/// The bus a trip belongs to — used to know which SignalR bus-group to
/// broadcast a position/lifecycle event to after a trip mutation.
public async Task<Guid?> GetBusIdAsync(Guid tripId, CancellationToken ct = default) =>
    (await QueryInlineAsync<Guid?>("SELECT BusId FROM dbo.Trips WHERE Id = @tripId", new { tripId }, ct)).FirstOrDefault();
```

- [ ] **Step 5: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Transport/TransportAuthorizationRepositoryTests.cs`.
Follow the exact pattern used in `tests/Sms.Tests.Integration/Transport/BusPositionTests.cs`
(`[Collection("sql")]`, `SqlServerFixture fx`, `WebApplicationFactory<Program>`,
raw Dapper `INSERT`s under `sp_set_session_context`). Read that file first to
match its exact `App()`/`Seed()`/`JwtTokenService.IssueAccess(...)` helper
shapes verbatim — this test needs the same helpers but calls repositories
directly (resolved from the `WebApplicationFactory`'s `IServiceProvider`)
rather than hitting an HTTP endpoint, since these are new repository methods
with no controller in front of them yet:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TransportAuthorizationRepositoryTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    [Fact]
    public async Task HasChildOnBusAsync_true_only_for_the_students_own_bus()
    {
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        const string admissionNo = "ADM-TEST-001";

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Students (Id, TenantId, Name, AdmissionNo) VALUES (@Id, @TenantId, 'Test Student', @AdmissionNo)",
                new { Id = studentId, TenantId = tenantId, AdmissionNo = admissionNo });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.StudentBusAssignments (Id, TenantId, StudentId, BusId) VALUES (@Id, @TenantId, @StudentId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, StudentId = studentId, BusId = busId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<StudentBusRepository>();

        (await repo.HasChildOnBusAsync(admissionNo, busId, default)).Should().BeTrue();
        (await repo.HasChildOnBusAsync(admissionNo, otherBusId, default)).Should().BeFalse();
        (await repo.HasChildOnBusAsync("NO-SUCH-ADMISSION", busId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task IsDutyTeacherForBusAsync_true_only_for_the_assigned_teacher()
    {
        var tenantId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var otherTeacherId = Guid.NewGuid();
        var busId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.BusAssignments (Id, TenantId, TeacherUserId, BusId) VALUES (@Id, @TenantId, @TeacherUserId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, TeacherUserId = teacherId, BusId = busId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        (await repo.IsDutyTeacherForBusAsync(teacherId, busId, default)).Should().BeTrue();
        (await repo.IsDutyTeacherForBusAsync(otherTeacherId, busId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveDriverOrConductorRoleByBusAsync_and_GetBusIdAsync()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var conductorId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
            await conn.ExecuteAsync(
                @"INSERT INTO dbo.Trips (Id, TenantId, BusId, DriverId, ConductorId, Direction, Status, StartedAt)
                  VALUES (@Id, @TenantId, @BusId, @DriverId, @ConductorId, 'pickup', 'live', SYSUTCDATETIME())",
                new { Id = tripId, TenantId = tenantId, BusId = busId, DriverId = driverId, ConductorId = conductorId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        (await repo.GetActiveDriverOrConductorRoleByBusAsync(busId, driverId, default)).Should().Be("driver");
        (await repo.GetActiveDriverOrConductorRoleByBusAsync(busId, conductorId, default)).Should().Be("conductor");
        (await repo.GetActiveDriverOrConductorRoleByBusAsync(busId, strangerId, default)).Should().BeNull();
        (await repo.GetBusIdAsync(tripId, default)).Should().Be(busId);
        (await repo.GetBusIdAsync(Guid.NewGuid(), default)).Should().BeNull();
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportAuthorizationRepositoryTests`
Expected: FAIL — compile error, the new repository methods don't exist yet
(if Steps 2-4 were skipped) or, if already applied, the test file itself is
new so this step just confirms the harness runs; if Steps 2-4 are done first,
skip ahead and use this run as the "make it pass" check instead.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportAuthorizationRepositoryTests`
Expected: PASS (3 tests). Requires a reachable SQL Server per
`SqlServerFixture` (`SMS_TEST_SQL_SERVER`/`SMS_TEST_SQL_CONNECTION` env vars,
or the `DESKTOP-TJL4SG6` default) — if none is reachable in this environment,
run `dotnet build tests/Sms.Tests.Integration` instead to confirm it compiles
cleanly, and note in the task report that the test could not be executed
against a live database.

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Modules.Transport/StudentBusModule.cs src/Sms.Modules.Transport/BusModule.cs src/Sms.Modules.Transport/TransportModule.cs tests/Sms.Tests.Integration/Transport/TransportAuthorizationRepositoryTests.cs
git commit -m "feat(transport): add repository primitives for bus-level authorization checks"
```

---

### Task 2: Live position snapshot (status + ETA derivation)

**Files:**
- Modify: `src/Sms.Modules.Transport/BusModule.cs` — add `BusLiveSnapshotResponse` and `GetLiveSnapshotAsync`.
- Test: `tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs` (new)

**Interfaces:**
- Consumes: `BusRepository.GetPositionAsync` (existing, returns `BusPositionResponse(BusId, CurrentStopIndex, Progress, Lat, Lng, NextStopName, EtaMinutes)`), `BusRepository`'s existing `CurrentTripIdAsync` private helper (already used by `GetPositionAsync`).
- Produces: `BusLiveSnapshotResponse` record and `BusRepository.GetLiveSnapshotAsync(Guid busId, CancellationToken ct = default) -> Task<BusLiveSnapshotResponse>` — consumed by Task 6 (`TripService`) as the `position_update` broadcast payload.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs`, same
pattern as Task 1's test file:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class BusLiveSnapshotTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    private async Task<(Guid tenantId, Guid busId, Guid tripId)> SeedBusWithLastPing(DateTime pingAt, double speedKmh)
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.Trips (Id, TenantId, BusId, Direction, Status, StartedAt)
              VALUES (@Id, @TenantId, @BusId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At)
              VALUES (@Id, @TenantId, @TripId, 12.1, 77.1, @SpeedKmh, 90, @At)",
            new { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, SpeedKmh = speedKmh, At = pingAt });
        return (tenantId, busId, tripId);
    }

    [Fact]
    public async Task Status_is_moving_when_a_fresh_fast_ping_exists()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-5), speedKmh: 25);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        var snapshot = await repo.GetLiveSnapshotAsync(busId, default);

        snapshot.Status.Should().Be("moving");
        snapshot.Lat.Should().Be(12.1);
        snapshot.SpeedKmh.Should().Be(25);
    }

    [Fact]
    public async Task Status_is_stopped_when_a_fresh_slow_ping_exists()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-5), speedKmh: 1);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        (await repo.GetLiveSnapshotAsync(busId, default)).Status.Should().Be("stopped");
    }

    [Fact]
    public async Task Status_is_offline_when_the_last_ping_is_older_than_60_seconds()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-90), speedKmh: 25);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        (await repo.GetLiveSnapshotAsync(busId, default)).Status.Should().Be("offline");
    }

    [Fact]
    public async Task Status_is_offline_when_the_bus_has_never_pinged()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        var snapshot = await repo.GetLiveSnapshotAsync(busId, default);
        snapshot.Status.Should().Be("offline");
        snapshot.Lat.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter BusLiveSnapshotTests`
Expected: FAIL — `BusLiveSnapshotResponse`/`GetLiveSnapshotAsync` don't exist yet.

- [ ] **Step 3: Add `BusLiveSnapshotResponse` and `GetLiveSnapshotAsync`**

In `src/Sms.Modules.Transport/BusModule.cs`, add this record at the namespace
level (next to `BusPositionResponse`) and this method to `BusRepository`
(next to `GetPositionAsync`):

```csharp
public sealed record BusLiveSnapshotResponse(
    Guid BusId, Guid? TripId,
    double? Lat, double? Lng, double SpeedKmh, double Heading,
    string Status, DateTime? LastUpdateAt,
    int? EtaNextStopMin, string? NextStopName);

private sealed record LiveSnapshotPingRow(double Lat, double Lng, double SpeedKmh, double Heading, DateTime At);

/// Full push payload for a bus's live-tracking subscribers: position, derived
/// status (moving/stopped/offline), and ETA — computed here once so every
/// consumer (parent app, teacher app, CRM) renders directly without
/// re-deriving status/ETA logic itself.
public async Task<BusLiveSnapshotResponse> GetLiveSnapshotAsync(Guid busId, CancellationToken ct = default)
{
    var tripId = await CurrentTripIdAsync(busId, ct);
    var ping = tripId is null ? null : (await QueryInlineAsync<LiveSnapshotPingRow>(
        "SELECT TOP 1 Lat, Lng, SpeedKmh, Heading, At FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At DESC",
        new { tripId }, ct)).FirstOrDefault();

    var position = await GetPositionAsync(busId, ct);

    string status;
    if (ping is null)
    {
        status = "offline";
    }
    else
    {
        var ageSeconds = (DateTime.UtcNow - ping.At).TotalSeconds;
        status = ageSeconds > 60 ? "offline" : ping.SpeedKmh > 3 ? "moving" : "stopped";
    }

    return new BusLiveSnapshotResponse(
        busId, tripId, ping?.Lat, ping?.Lng, ping?.SpeedKmh ?? 0, ping?.Heading ?? 0,
        status, ping?.At, position.EtaMinutes, position.NextStopName);
}
```

**Note:** confirm `CurrentTripIdAsync`'s exact signature (it is referenced
inside the existing `GetPositionAsync` per prior investigation, but read it
directly in `BusModule.cs` before writing this method to match its real
return type — `Task<Guid?>` is expected but verify).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter BusLiveSnapshotTests`
Expected: PASS (4 tests). If no live SQL Server is reachable, run
`dotnet build tests/Sms.Tests.Integration` instead and note the limitation.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Modules.Transport/BusModule.cs tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs
git commit -m "feat(transport): add BusLiveSnapshotResponse with moving/stopped/offline status derivation"
```

---

### Task 3: `ITransportAuthorizationResolver`

**Files:**
- Create: `src/Sms.Application/Services/Transport/ITransportAuthorizationResolver.cs`
- Create: `src/Sms.Application/Services/Transport/TransportAuthorizationResolver.cs`
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` — register the new service.
- Test: `tests/Sms.Tests.Integration/Transport/TransportAuthorizationResolverTests.cs` (new)

**Interfaces:**
- Consumes: `StudentBusRepository.BusExistsAsync` (existing), `StudentBusRepository.HasChildOnBusAsync` (Task 1), `BusRepository.IsDutyTeacherForBusAsync` (Task 1), `TripRepository.GetActiveDriverOrConductorRoleByBusAsync` (Task 1), `IAuthDao.GetByIdAsync(Guid userId, CancellationToken ct) -> Task<User?>` where `User.StudentId` is the parent's linked admission number (existing, used by `StudentBusService`), `ITenantContext.Set(Guid? tenantId, Guid? userId, bool isPlatform)` (existing), `Policies.*` constants (existing).
- Produces: `ITransportAuthorizationResolver.CanViewBusAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid busId, CancellationToken ct = default) -> Task<bool>` — consumed by Task 5's hub `JoinBus` method.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Transport/TransportAuthorizationResolverTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TransportAuthorizationResolverTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    [Fact]
    public async Task Principal_can_view_any_bus_in_their_own_tenant_but_not_another_tenants()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(principalId, tenantId, [Policies.Principal], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(principalId, otherTenantId, [Policies.Principal], busId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Teacher_can_view_only_their_assigned_duty_bus()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.BusAssignments (Id, TenantId, TeacherUserId, BusId) VALUES (@Id, @TenantId, @TeacherUserId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, TeacherUserId = teacherId, BusId = busId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(teacherId, tenantId, [Policies.Teacher], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(teacherId, tenantId, [Policies.Teacher], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Driver_can_view_only_the_bus_of_their_own_active_trip()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                @"INSERT INTO dbo.Trips (Id, TenantId, BusId, DriverId, Direction, Status, StartedAt)
                  VALUES (@Id, @TenantId, @BusId, @DriverId, 'pickup', 'live', SYSUTCDATETIME())",
                new { Id = Guid.NewGuid(), TenantId = tenantId, BusId = busId, DriverId = driverId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(driverId, tenantId, [Policies.Driver], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(driverId, tenantId, [Policies.Driver], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Parent_can_view_only_their_own_childs_bus()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        const string admissionNo = "ADM-PARENT-001";

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Students (Id, TenantId, Name, AdmissionNo) VALUES (@Id, @TenantId, 'Kid', @AdmissionNo)",
                new { Id = studentId, TenantId = tenantId, AdmissionNo = admissionNo });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.StudentBusAssignments (Id, TenantId, StudentId, BusId) VALUES (@Id, @TenantId, @StudentId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, StudentId = studentId, BusId = busId });
            // The parent's Users row is linked to their child via Users.StudentId = admission number
            // (see StudentBusService.GetMyChildrenBusAsync for this same pattern). Read the exact
            // Users table columns before writing this INSERT — StudentId, TenantId, and whatever
            // NOT NULL columns Users requires (Email/Role etc.) must be filled in to satisfy the schema.
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Users (Id, TenantId, StudentId, Email, Role) VALUES (@Id, @TenantId, @AdmissionNo, @Email, 'student.parent')",
                new { Id = parentId, TenantId = tenantId, AdmissionNo = admissionNo, Email = $"parent-{parentId}@test.local" });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(parentId, tenantId, [Policies.StudentOrParent], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(parentId, tenantId, [Policies.StudentOrParent], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_role_is_denied()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }
        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(Guid.NewGuid(), tenantId, ["some.other.role"], busId, default)).Should().BeFalse();
    }
}
```

If `dbo.Users`'s exact schema (columns/NOT NULL constraints) doesn't match
the `INSERT` above once you read the real migration, adjust the seed to match
— the columns used elsewhere in this codebase (`StudentBusService.GetMyChildrenBusAsync`
reads `me.StudentId`) confirm `StudentId` and presumably `Email`/`Role` exist,
but confirm exact NOT NULL columns from `db/Sms.Migrations` before finalizing
this seed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportAuthorizationResolverTests`
Expected: FAIL — `ITransportAuthorizationResolver` doesn't exist yet.

- [ ] **Step 3: Write the interface**

```csharp
// src/Sms.Application/Services/Transport/ITransportAuthorizationResolver.cs
namespace Sms.Application.Services.Transport;

/// Single source of truth for "can this authenticated caller see this bus's
/// live position." Every check resolves server-side from the caller's own
/// identity (userId/tenantId/roles from their JWT) and existing DB
/// relationships — never from a client-supplied busId being trusted as
/// proof of access.
public interface ITransportAuthorizationResolver
{
    Task<bool> CanViewBusAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid busId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the implementation**

```csharp
// src/Sms.Application/Services/Transport/TransportAuthorizationResolver.cs
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Transport;

public sealed class TransportAuthorizationResolver(
    TripRepository trips,
    BusRepository buses,
    StudentBusRepository studentBus,
    IAuthDao users,
    ITenantContext tenant) : ITransportAuthorizationResolver
{
    public async Task<bool> CanViewBusAsync(
        Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles,
        Guid busId, CancellationToken ct = default)
    {
        // Hub method invocations don't flow through the HTTP-request middleware
        // that normally populates ITenantContext, so it must be set explicitly
        // here before any repository call relies on RLS session context —
        // same pattern as AbsenceAlertWorker's manual `tenant.Set(...)`.
        tenant.Set(callerTenantId, callerUserId, isPlatform: false);

        if (callerRoles.Contains(Policies.Principal) || callerRoles.Contains(Policies.SchoolAdmin) || callerRoles.Contains(Policies.SchoolOwner))
            return await studentBus.BusExistsAsync(busId, ct);

        if (callerRoles.Contains(Policies.Teacher))
            return await buses.IsDutyTeacherForBusAsync(callerUserId, busId, ct);

        if (callerRoles.Contains(Policies.Driver) || callerRoles.Contains("conductor"))
            return await trips.GetActiveDriverOrConductorRoleByBusAsync(busId, callerUserId, ct) is not null;

        if (callerRoles.Contains(Policies.StudentOrParent) || callerRoles.Contains("parent") || callerRoles.Contains("student"))
        {
            var me = await users.GetByIdAsync(callerUserId, ct);
            if (me?.StudentId is not { Length: > 0 } admissionNo) return false;
            return await studentBus.HasChildOnBusAsync(admissionNo, busId, ct);
        }

        return false;
    }
}
```

**Note:** confirm `IAuthDao`'s exact namespace and `GetByIdAsync` signature
(prior investigation found it used as `await users.GetByIdAsync(uid, ct)`
returning a type with a `.StudentId` property, inside
`src/Sms.Application/Services/Transport/StudentBusService.cs` — read that
file's `using` statements to get `IAuthDao`'s real namespace before finalizing
this file's `using` list).

- [ ] **Step 5: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, add next to the
existing `builder.Services.AddScoped<ITransportFleetBroadcaster, TransportFleetBroadcaster>();`
line:

```csharp
builder.Services.AddScoped<ITransportAuthorizationResolver, TransportAuthorizationResolver>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportAuthorizationResolverTests`
Expected: PASS (5 tests). If no live SQL Server is reachable, run
`dotnet build` on the solution instead and note the limitation.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Application/Services/Transport/ITransportAuthorizationResolver.cs src/Sms.Application/Services/Transport/TransportAuthorizationResolver.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Transport/TransportAuthorizationResolverTests.cs
git commit -m "feat(transport): add ITransportAuthorizationResolver implementing the bus-visibility matrix"
```

---

### Task 4: Extend `ITransportFleetBroadcaster` with per-bus events

**Files:**
- Modify: `src/Sms.Application/Services/Transport/ITransportFleetBroadcaster.cs`
- Modify: `src/Sms.Application/Services/Transport/NoOpTransportFleetBroadcaster.cs`
- Modify: `src/Sms.Api/Services/TransportFleetBroadcaster.cs`
- Modify: `src/Sms.Api/Hubs/TransportFleetHub.cs` — add `BusGroup` static helper only (hub authorization changes are Task 5).

**Interfaces:**
- Consumes: `BusLiveSnapshotResponse` (Task 2), `IHubContext<TransportFleetHub>` (existing).
- Produces: four new `ITransportFleetBroadcaster` methods and `TransportFleetHub.BusGroup(Guid busId) -> string` — consumed by Task 6 (`TripService`) and Task 7 (offline sweep worker).

- [ ] **Step 1: Add the `BusGroup` static helper to `TransportFleetHub`**

In `src/Sms.Api/Hubs/TransportFleetHub.cs`, add next to the existing
`TenantGroup` static method:

```csharp
public static string BusGroup(Guid busId) => $"bus:{busId}";
```

- [ ] **Step 2: Extend `ITransportFleetBroadcaster`**

In `src/Sms.Application/Services/Transport/ITransportFleetBroadcaster.cs`,
add these four methods to the interface:

```csharp
Task BroadcastPositionAsync(Guid busId, BusLiveSnapshotResponse snapshot, CancellationToken ct = default);
Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default);
Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default);
Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default);
```

- [ ] **Step 3: Implement them in `NoOpTransportFleetBroadcaster`**

In `src/Sms.Application/Services/Transport/NoOpTransportFleetBroadcaster.cs`,
add matching no-op implementations (mirror however `BroadcastFleetAsync` is
already implemented there — likely `Task.CompletedTask` for each):

```csharp
public Task BroadcastPositionAsync(Guid busId, BusLiveSnapshotResponse snapshot, CancellationToken ct = default) => Task.CompletedTask;
public Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default) => Task.CompletedTask;
public Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default) => Task.CompletedTask;
public Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default) => Task.CompletedTask;
```

- [ ] **Step 4: Implement them in `TransportFleetBroadcaster`**

In `src/Sms.Api/Services/TransportFleetBroadcaster.cs`, add:

```csharp
public async Task BroadcastPositionAsync(Guid busId, BusLiveSnapshotResponse snapshot, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("position_update", snapshot, ct);

public async Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("trip_started",
        new { busId, tripId, driverId, conductorId, direction, startedAt }, ct);

public async Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("trip_ended",
        new { busId, tripId, endedAt }, ct);

public async Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("status_changed",
        new { busId, tripId, status }, ct);
```

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build src/Sms.Api`
Expected: builds cleanly — this task has no new automated test of its own
(it's plumbing consumed by Tasks 6 and 7's tests), but a clean build across
`Sms.Application`, `Sms.Api` confirms every implementer of the interface
(including `NoOpTransportFleetBroadcaster`) was updated.

Run: `dotnet build` (whole solution)
Expected: 0 errors — this specifically confirms no other `ITransportFleetBroadcaster`
implementer was missed.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/Transport/ITransportFleetBroadcaster.cs src/Sms.Application/Services/Transport/NoOpTransportFleetBroadcaster.cs src/Sms.Api/Services/TransportFleetBroadcaster.cs src/Sms.Api/Hubs/TransportFleetHub.cs
git commit -m "feat(transport): extend ITransportFleetBroadcaster with per-bus position/lifecycle events"
```

---

### Task 5: `TransportFleetHub` — authorized per-bus groups

**Files:**
- Modify: `src/Sms.Api/Hubs/TransportFleetHub.cs`
- Test: `tests/Sms.Tests.Integration/Transport/TransportFleetHubTests.cs` (new)
- Modify: `tests/Sms.Tests.Integration/Sms.Tests.Integration.csproj` — add `Microsoft.AspNetCore.SignalR.Client` package reference, if not already present.

**Interfaces:**
- Consumes: `ITransportAuthorizationResolver.CanViewBusAsync` (Task 3), `Policies.*` (existing).
- Produces: `JoinBus(Guid busId) -> Task<bool>`, `LeaveBus(Guid busId) -> Task` hub methods — this is the client-facing surface every consumer app (CRM, teacher-app, parent app) will call in later specs.

- [ ] **Step 1: Check for the SignalR client test package**

Run: `grep -n "SignalR.Client" tests/Sms.Tests.Integration/Sms.Tests.Integration.csproj`

If no match, add it:

Run: `dotnet add tests/Sms.Tests.Integration/Sms.Tests.Integration.csproj package Microsoft.AspNetCore.SignalR.Client`

- [ ] **Step 2: Write the failing integration test**

This codebase has no existing hub-connection test to mirror — this
introduces that pattern, following the same JWT-issuance style as
`BusPositionTests.cs`'s `TeacherClient` helper, but connecting a real
`HubConnection` instead of an `HttpClient`.

Create `tests/Sms.Tests.Integration/Transport/TransportFleetHubTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Dapper;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TransportFleetHubTests(SqlServerFixture fx)
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static string IssueToken(Guid userId, Guid tenantId, string role)
    {
        // Match JwtTokenService's real constructor/options shape exactly —
        // read src/Sms.Shared.Kernel/Auth/JwtTokenService.cs before finalizing
        // this call if the options shape differs from what's assumed here.
        var jwt = new JwtTokenService(new JwtOptions { SigningKey = Key }, new SystemClock());
        return jwt.IssueAccess(userId, tenantId, [role], isPlatform: false);
    }

    private static async Task<HubConnection> ConnectAsync(WebApplicationFactory<Program> app, string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{app.Server.BaseAddress}hubs/transport-fleet", opts =>
            {
                opts.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                opts.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task JoinBus_returns_true_for_the_duty_teacher_and_false_for_a_stranger()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.BusAssignments (Id, TenantId, TeacherUserId, BusId) VALUES (@Id, @TenantId, @TeacherUserId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, TeacherUserId = teacherId, BusId = busId });
        }

        await using var app = App();
        await using var teacherConn = await ConnectAsync(app, IssueToken(teacherId, tenantId, Policies.Teacher));
        await using var strangerConn = await ConnectAsync(app, IssueToken(strangerId, tenantId, Policies.Teacher));

        (await teacherConn.InvokeAsync<bool>("JoinBus", busId)).Should().BeTrue();
        (await strangerConn.InvokeAsync<bool>("JoinBus", busId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_denied_JoinBus_does_not_disconnect_the_connection()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }

        await using var app = App();
        await using var connection = await ConnectAsync(app, IssueToken(strangerId, tenantId, Policies.Teacher));

        (await connection.InvokeAsync<bool>("JoinBus", busId)).Should().BeFalse();
        connection.State.Should().Be(HubConnectionState.Connected);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportFleetHubTests`
Expected: FAIL — `JoinBus`/`LeaveBus` hub methods don't exist yet, and/or the
hub still requires `Policies.Principal` so the `Teacher`-issued token can't
even connect.

- [ ] **Step 4: Modify `TransportFleetHub`**

Replace the hub's class-level attribute and body in
`src/Sms.Api/Hubs/TransportFleetHub.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Hubs;

[Authorize]
public sealed class TransportFleetHub(ITransportAuthorizationResolver authz) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var roles = Context.User?.FindAll("role").Select(c => c.Value).ToArray() ?? [];
        // Only Principal-tier callers get the tenant-wide fleet feed automatically —
        // everyone else (teacher/parent/driver) must call JoinBus for their one
        // authorized bus.
        if (tenantId is not null && (roles.Contains(Policies.Principal) || roles.Contains(Policies.SchoolAdmin) || roles.Contains(Policies.SchoolOwner)))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }

    /// Joins the caller to this bus's live-position group iff
    /// ITransportAuthorizationResolver says they're allowed to see it.
    /// Returns false (not a thrown exception) on denial, so a caller with
    /// multiple pending JoinBus calls doesn't lose its whole connection over
    /// one unauthorized bus.
    public async Task<bool> JoinBus(Guid busId)
    {
        var (userId, tenantId, roles) = CallerClaims();
        if (userId is null || tenantId is null) return false;
        if (!await authz.CanViewBusAsync(userId.Value, tenantId.Value, roles, busId))
            return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, BusGroup(busId));
        return true;
    }

    public Task LeaveBus(Guid busId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BusGroup(busId));

    private (Guid? UserId, Guid? TenantId, string[] Roles) CallerClaims()
    {
        var uid = Context.User?.FindFirst("sub")?.Value;
        var tid = Context.User?.FindFirst("tenant_id")?.Value;
        var roles = Context.User?.FindAll("role").Select(c => c.Value).ToArray() ?? [];
        return (Guid.TryParse(uid, out var u) ? u : null, Guid.TryParse(tid, out var t) ? t : null, roles);
    }

    public static string TenantGroup(string tenantId) => $"transport-fleet:{tenantId}";
    public static string BusGroup(Guid busId) => $"bus:{busId}";
}
```

(`BusGroup` was added in Task 4 — this replaces that earlier version only if
Task 4 placed it outside the constructor-injected class shape; otherwise this
is the first time the hub gets a constructor, since the original had none.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TransportFleetHubTests`
Expected: PASS (2 tests). If no live SQL Server is reachable, run
`dotnet build` instead and note the limitation — this test in particular
also depends on `Program` being accessible to the test project and the
in-memory `TestServer`'s `CreateHandler()` correctly proxying a WebSocket-ish
SignalR connection over HTTP long-polling (the default transport
`HubConnectionBuilder` negotiates when given an `HttpMessageHandlerFactory`
pointed at a `TestServer` — this is the standard ASP.NET Core pattern for
testing hubs in-process, but confirm no additional
`.WithUrl(..., HttpTransportType.LongPolling)` hint is needed if the default
negotiation fails against `TestServer`).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Api/Hubs/TransportFleetHub.cs tests/Sms.Tests.Integration/Transport/TransportFleetHubTests.cs tests/Sms.Tests.Integration/Sms.Tests.Integration.csproj
git commit -m "feat(transport): loosen TransportFleetHub to authenticated callers, add authorized JoinBus/LeaveBus"
```

---

### Task 6: Wire `TripService` to broadcast per-bus events

**Files:**
- Modify: `src/Sms.Application/Services/Transport/TripService.cs`
- Test: extend `tests/Sms.Tests.Integration/Transport/BusPositionTests.cs`'s sibling trip test file if one exists (`ls tests/Sms.Tests.Integration/Transport/Trip*Tests.cs`), otherwise create `tests/Sms.Tests.Integration/Transport/TripServiceBroadcastTests.cs` (new).

**Interfaces:**
- Consumes: `TripRepository.GetBusIdAsync` (Task 1), `BusRepository.GetLiveSnapshotAsync` (Task 2), `ITransportFleetBroadcaster.BroadcastPositionAsync`/`BroadcastTripStartedAsync`/`BroadcastTripEndedAsync` (Task 4).
- Produces: nothing consumed by later tasks — this is an integration point.

- [ ] **Step 1: Check for an existing TripService test file**

Run: `ls tests/Sms.Tests.Integration/Transport/*Trip* tests/Sms.Tests.Integration/Transport/*Service*`

If a test already exercises `TripController`'s start/ping/end endpoints
end-to-end, extend it with the assertions below instead of creating a new
file (a `TransportFleetBroadcaster`-based assertion needs an actual
`HubConnection` subscribed and listening for the event, similar to Task 5's
pattern, rather than a mocked broadcaster — this codebase does not mock).

- [ ] **Step 2: Write the failing test**

Create (or extend) `tests/Sms.Tests.Integration/Transport/TripServiceBroadcastTests.cs`,
combining the JWT/seed helpers from `BusPositionTests.cs` (HTTP calls to
start/ping/end) with the `HubConnection` pattern from Task 5 (subscribing to
`bus:{busId}` and asserting a `position_update` arrives):

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Dapper;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TripServiceBroadcastTests(SqlServerFixture fx)
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static string IssueToken(Guid userId, Guid tenantId, string role) =>
        new JwtTokenService(new JwtOptions { SigningKey = Key }, new SystemClock())
            .IssueAccess(userId, tenantId, [role], isPlatform: false);

    [Fact]
    public async Task Starting_a_trip_broadcasts_trip_started_to_the_buss_group()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo, DriverStaffId) VALUES (@Id, @TenantId, 'BUS-1', @DriverId)",
                new { Id = busId, TenantId = tenantId, DriverId = driverId });
        }

        await using var app = App();
        var driverToken = IssueToken(driverId, tenantId, Policies.Driver);

        var listenerConn = new HubConnectionBuilder()
            .WithUrl($"{app.Server.BaseAddress}hubs/transport-fleet", opts =>
            {
                opts.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                opts.AccessTokenProvider = () => Task.FromResult<string?>(driverToken);
            })
            .Build();
        var startedTcs = new TaskCompletionSource<object>();
        listenerConn.On<object>("trip_started", payload => startedTcs.TrySetResult(payload));
        await listenerConn.StartAsync();
        (await listenerConn.InvokeAsync<bool>("JoinBus", busId)).Should().BeTrue();

        var httpClient = app.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", driverToken);
        var startResponse = await httpClient.PostAsJsonAsync("/v1/staff/trips/start", new { direction = "pickup" });
        startResponse.IsSuccessStatusCode.Should().BeTrue();

        var received = await Task.WhenAny(startedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        received.Should().Be(startedTcs.Task, "trip_started should have been pushed to the bus group within 5s");

        await listenerConn.DisposeAsync();
    }
}
```

**Note:** confirm the exact `POST /v1/staff/trips/start` route and request
body shape against `TripController.cs` before finalizing this test — the
prior investigation found `TripController` at `[Route("v1/staff")]` handling
driver/conductor trip start/ping/end, but its exact action route segment and
`StartTripRequest` JSON shape (`{ routeId, busNo, direction }` per the C#
record) should be verified directly.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripServiceBroadcastTests`
Expected: FAIL — no `trip_started` push happens yet.

- [ ] **Step 4: Wire the broadcasts into `TripService`**

In `src/Sms.Application/Services/Transport/TripService.cs`, add `BusRepository buses`
to the primary constructor parameter list, then modify the three methods:

```csharp
public sealed class TripService(
    TripRepository repo, BusRepository buses, ITenantContext tenant,
    ITransportFleetBroadcaster fleetBroadcaster, ILiveBroadcaster live, IClock clock) : ITripService
{
    public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var trip = (await repo.StartAsync(tid, uid, req, ct))!;
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        if (await repo.GetBusIdAsync(trip.Id, ct) is { } busId)
            await fleetBroadcaster.BroadcastTripStartedAsync(busId, trip.Id, trip.DriverId, trip.ConductorId, trip.Direction, trip.StartedAt ?? clock.UtcNow, ct);
        return ApiResult<TripResponse>.Ok(WithActiveBroadcaster(trip), 201);
    }

    public async Task<ApiResult> IngestPingsAsync(Guid tripId, BulkPingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var role = await repo.GetParticipantRoleAsync(tripId, uid, ct);
        if (role is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        await repo.IngestPingsAsync(tid, tripId, req.Pings, ct);
        await repo.MarkPingAsync(tripId, role, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
        {
            var snapshot = await buses.GetLiveSnapshotAsync(busId, ct);
            await fleetBroadcaster.BroadcastPositionAsync(busId, snapshot, ct);
        }
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "not your trip"), 403);
        var busId = await repo.GetBusIdAsync(tripId, ct);
        var summary = await repo.EndAsync(tripId, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        if (busId is { } bid)
            await fleetBroadcaster.BroadcastTripEndedAsync(bid, tripId, clock.UtcNow, ct);
        return ApiResult<TripSummaryResponse>.Ok(summary);
    }

    // ... GetCurrentAsync, GetAssignmentAsync, GetRosterAsync, ListBoardingAsync,
    // UpsertBoardingAsync, WithActiveBroadcaster stay exactly as they are today —
    // do not modify them.
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripServiceBroadcastTests`
Expected: PASS. If no live SQL Server is reachable, run `dotnet build`
instead and note the limitation.

Run: `dotnet test tests/Sms.Tests.Integration --filter Trip` (broader filter)
Expected: all pre-existing `TripService`/`TripController` tests still pass —
confirms the new `BusRepository` constructor dependency and the added
broadcast calls didn't change any existing behavior or response shape.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/Transport/TripService.cs tests/Sms.Tests.Integration/Transport/TripServiceBroadcastTests.cs
git commit -m "feat(transport): broadcast position_update/trip_started/trip_ended from TripService"
```

---

### Task 7: Offline-sweep background worker

**Files:**
- Create: `src/Sms.Application/Services/Transport/TransportOfflineSweepRules.cs` (pure logic)
- Test: `tests/Sms.Tests.Unit/Transport/TransportOfflineSweepRulesTests.cs` (new)
- Create: `src/Sms.Api/Workers/TransportOfflineSweepWorker.cs`
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` — add `TripRepository.GetStaleActiveTripsAsync`.
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` — register the hosted service.

**Interfaces:**
- Consumes: `TripRepository.GetStaleActiveTripsAsync` (this task), `ITransportFleetBroadcaster.BroadcastStatusChangedAsync` (Task 4), `ITenantContext.Set` (existing).
- Produces: `TransportOfflineSweepRules.ComputeTransitions(IReadOnlySet<Guid> previouslyOffline, IReadOnlySet<Guid> currentlyStale) -> (IReadOnlyList<Guid> ToNotify, IReadOnlyList<Guid> ToClear)` — pure, no later task consumes it directly, but it's the unit-testable core of the worker below.

- [ ] **Step 1: Write the failing pure-logic unit test**

Create `tests/Sms.Tests.Unit/Transport/TransportOfflineSweepRulesTests.cs`
(no DB, plain xUnit + FluentAssertions, matching the style of
`tests/Sms.Tests.Unit/Transport/TripBroadcasterRulesTests.cs`):

```csharp
using Sms.Application.Services.Transport;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Unit.Transport;

public class TransportOfflineSweepRulesTests
{
    [Fact]
    public void A_newly_stale_trip_not_previously_offline_should_be_notified()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid>(),
            currentlyStale: new HashSet<Guid> { tripId });

        toNotify.Should().ContainSingle().Which.Should().Be(tripId);
        toClear.Should().BeEmpty();
    }

    [Fact]
    public void A_trip_still_stale_from_last_sweep_should_not_be_notified_again()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { tripId },
            currentlyStale: new HashSet<Guid> { tripId });

        toNotify.Should().BeEmpty();
        toClear.Should().BeEmpty();
    }

    [Fact]
    public void A_trip_that_recovered_should_be_cleared_so_it_can_be_notified_again_later()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { tripId },
            currentlyStale: new HashSet<Guid>());

        toNotify.Should().BeEmpty();
        toClear.Should().ContainSingle().Which.Should().Be(tripId);
    }

    [Fact]
    public void Unrelated_trips_are_independent()
    {
        var stillStale = Guid.NewGuid();
        var recovered = Guid.NewGuid();
        var newlyStale = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { stillStale, recovered },
            currentlyStale: new HashSet<Guid> { stillStale, newlyStale });

        toNotify.Should().ContainSingle().Which.Should().Be(newlyStale);
        toClear.Should().ContainSingle().Which.Should().Be(recovered);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter TransportOfflineSweepRulesTests`
Expected: FAIL — `TransportOfflineSweepRules` doesn't exist yet.

- [ ] **Step 3: Write `TransportOfflineSweepRules`**

```csharp
// src/Sms.Application/Services/Transport/TransportOfflineSweepRules.cs
namespace Sms.Application.Services.Transport;

/// Pure "fire once until recovered" transition logic for the offline sweep:
/// a trip crossing the stale threshold is notified exactly once, and only
/// re-notified after it's seen fresh again (removed from currentlyStale)
/// and then goes stale a second time.
public static class TransportOfflineSweepRules
{
    public static (IReadOnlyList<Guid> ToNotify, IReadOnlyList<Guid> ToClear) ComputeTransitions(
        IReadOnlySet<Guid> previouslyOffline, IReadOnlySet<Guid> currentlyStale)
    {
        var toNotify = currentlyStale.Where(id => !previouslyOffline.Contains(id)).ToList();
        var toClear = previouslyOffline.Where(id => !currentlyStale.Contains(id)).ToList();
        return (toNotify, toClear);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter TransportOfflineSweepRulesTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Add `GetStaleActiveTripsAsync` to `TripRepository`**

In `src/Sms.Modules.Transport/TransportModule.cs`, add this public record
(top-level, alongside the other DTOs) and method to `TripRepository`:

```csharp
public sealed record StaleTripRow(Guid TripId, Guid BusId, Guid TenantId, DateTime? LastPingAt);

/// Every currently-live trip whose most recent driver-or-conductor ping is
/// older than staleAfter (or has never pinged at all). Runs under a
/// platform-level ITenantContext (IsPlatform = true) since this must scan
/// across every tenant, mirroring AbsenceAlertWorker's cross-tenant sweep
/// pattern — verify against src/Sms.Api/Workers/AbsenceAlertWorker.cs that
/// rls.fn_tenant_predicate actually bypasses RLS filtering when IsPlatform
/// is true before relying on this in production.
public async Task<IReadOnlyList<StaleTripRow>> GetStaleActiveTripsAsync(TimeSpan staleAfter, CancellationToken ct = default)
{
    var rows = await QueryInlineAsync<StaleTripRow>(
        @"SELECT Id AS TripId, BusId, TenantId,
                 (SELECT MAX(v) FROM (VALUES (DriverLastPingAt), (ConductorLastPingAt)) AS x(v)) AS LastPingAt
          FROM dbo.Trips
          WHERE Status = 'live' AND BusId IS NOT NULL", null, ct);
    var cutoff = DateTime.UtcNow - staleAfter;
    return rows.Where(r => r.LastPingAt is null || r.LastPingAt < cutoff).ToList();
}
```

- [ ] **Step 5b: Write a failing integration test for the stale-threshold query itself**

`TransportOfflineSweepRules` only covers the pure notify-once logic — this
step covers the actual 60-second staleness detection the spec requires.
Create `tests/Sms.Tests.Integration/Transport/GetStaleActiveTripsAsyncTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class GetStaleActiveTripsAsyncTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    private async Task<Guid> SeedLiveTrip(Guid tenantId, DateTime? driverLastPingAt, DateTime? conductorLastPingAt)
    {
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.Trips (Id, TenantId, BusId, Direction, Status, StartedAt, DriverLastPingAt, ConductorLastPingAt)
              VALUES (@Id, @TenantId, @BusId, 'pickup', 'live', SYSUTCDATETIME(), @DriverLastPingAt, @ConductorLastPingAt)",
            new { Id = tripId, TenantId = tenantId, BusId = busId, DriverLastPingAt = driverLastPingAt, ConductorLastPingAt = conductorLastPingAt });
        return tripId;
    }

    [Fact]
    public async Task Trips_with_no_ping_within_60_seconds_are_returned_as_stale()
    {
        var tenantId = Guid.NewGuid();
        var staleTripId = await SeedLiveTrip(tenantId, DateTime.UtcNow.AddSeconds(-90), null);
        var freshTripId = await SeedLiveTrip(tenantId, DateTime.UtcNow.AddSeconds(-5), null);
        var neverPingedTripId = await SeedLiveTrip(tenantId, null, null);

        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(null, null, isPlatform: true);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        var stale = await repo.GetStaleActiveTripsAsync(TimeSpan.FromSeconds(60), default);
        var staleIds = stale.Select(s => s.TripId).ToHashSet();

        staleIds.Should().Contain(staleTripId);
        staleIds.Should().Contain(neverPingedTripId);
        staleIds.Should().NotContain(freshTripId);
    }

    [Fact]
    public async Task Uses_the_more_recent_of_driver_or_conductor_ping()
    {
        var tenantId = Guid.NewGuid();
        // Driver went silent, but conductor pinged 5s ago — the trip is NOT stale.
        var tripId = await SeedLiveTrip(tenantId, DateTime.UtcNow.AddSeconds(-90), DateTime.UtcNow.AddSeconds(-5));

        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(null, null, isPlatform: true);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        var stale = await repo.GetStaleActiveTripsAsync(TimeSpan.FromSeconds(60), default);
        stale.Select(s => s.TripId).Should().NotContain(tripId);
    }
}
```

Run: `dotnet test tests/Sms.Tests.Integration --filter GetStaleActiveTripsAsyncTests`
Expected: FAIL until Step 5's `GetStaleActiveTripsAsync` exists (if this step
runs before Step 5, that's the expected failure reason; if after, it should
already pass — run it regardless to confirm).

- [ ] **Step 5c: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter GetStaleActiveTripsAsyncTests`
Expected: PASS (2 tests). If no live SQL Server is reachable, run
`dotnet build tests/Sms.Tests.Integration` instead and note the limitation.

- [ ] **Step 6: Write `TransportOfflineSweepWorker`**

```csharp
// src/Sms.Api/Workers/TransportOfflineSweepWorker.cs
using System.Collections.Concurrent;
using Sms.Application.Services.Transport;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Workers;

/// Periodically finds live trips with no recent ping and broadcasts a
/// status_changed(offline) event to that bus's group — the one state
/// transition no ping will ever announce on its own. Mirrors
/// AbsenceAlertWorker's scope-per-sweep + platform-context pattern.
public sealed class TransportOfflineSweepWorker(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<TransportOfflineSweepWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _poll = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("TransportOfflineSweep:PollSeconds") ?? 20, 5, 300));
    private readonly ConcurrentDictionary<Guid, byte> _offline = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transport offline sweep failed");
            }
            await Task.Delay(_poll, stoppingToken);
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(null, null, isPlatform: true);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<ITransportFleetBroadcaster>();

        var stale = await repo.GetStaleActiveTripsAsync(StaleAfter, ct);
        var staleIds = stale.Select(s => s.TripId).ToHashSet();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            new HashSet<Guid>(_offline.Keys), staleIds);

        foreach (var tripId in toNotify)
        {
            var trip = stale.First(s => s.TripId == tripId);
            _offline[tripId] = 0;
            await broadcaster.BroadcastStatusChangedAsync(trip.BusId, tripId, "offline", ct);
        }
        foreach (var tripId in toClear)
            _offline.TryRemove(tripId, out _);
    }
}
```

- [ ] **Step 7: Register the hosted service**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, add next to the
existing `builder.Services.AddHostedService<EmailDispatchWorker>();` line:

```csharp
builder.Services.AddHostedService<Sms.Api.Workers.TransportOfflineSweepWorker>();
```

- [ ] **Step 8: Build to verify no compile errors**

Run: `dotnet build`
Expected: 0 errors. The stale-threshold detection is covered by Step 5b/5c's
integration test and the notify-once logic by Step 1-4's unit test; the
`TransportOfflineSweepWorker` class itself (the `BackgroundService` loop
wiring) has no dedicated test — it is a thin composition of already-tested
pieces. Note this remaining thin-wrapper gap in the task report rather than
skipping the build check.

- [ ] **Step 9: Commit**

```bash
git add src/Sms.Application/Services/Transport/TransportOfflineSweepRules.cs tests/Sms.Tests.Unit/Transport/TransportOfflineSweepRulesTests.cs src/Sms.Api/Workers/TransportOfflineSweepWorker.cs src/Sms.Modules.Transport/TransportModule.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Transport/GetStaleActiveTripsAsyncTests.cs
git commit -m "feat(transport): add offline-sweep background worker with fire-once-until-recovered semantics"
```

---

### Task 8: Full verification pass

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass, including every test added in Tasks 1-7. If a live
SQL Server is unreachable in this environment, run `dotnet test tests/Sms.Tests.Unit`
(no DB dependency) and confirm it's green, and separately confirm
`dotnet build tests/Sms.Tests.Integration` compiles cleanly — note the
inability to execute integration tests as a real limitation when reporting
completion, per this plan's established pattern for DB-dependent test runs.

- [ ] **Step 3: Confirm the authorization matrix end-to-end**

Re-read `docs/superpowers/specs/2026-09-04-transport-live-tracking-authorization-design.md`'s
"Authorization Matrix" table and confirm each row has a passing test:
Driver/Conductor (Task 3's resolver test + Task 5's hub test), Duty Teacher
(same), Principal/SchoolAdmin/SchoolOwner (same), Parent (same), "anyone
else" denied (Task 3's `Unknown_role_is_denied`).

- [ ] **Step 4: Confirm no regressions to existing transport endpoints**

Run: `dotnet test tests/Sms.Tests.Integration --filter Transport`
Expected: all pre-existing transport tests (fleet, bus position, parent
transport, trip lifecycle) still pass unchanged — confirms the new
`TripService` constructor dependency and hub authorize-attribute change
didn't break any existing consumer.

- [ ] **Step 5: Final commit (if anything was fixed during verification)**

```bash
git add -A
git commit -m "fix(transport): address issues found during full verification pass"
```

(Skip this step if verification found nothing to fix.)
