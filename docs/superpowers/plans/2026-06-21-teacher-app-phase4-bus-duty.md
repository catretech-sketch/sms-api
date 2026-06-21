# Teacher+Principal App — Phase 4: Bus-Duty Teacher View (core) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a teacher with bus duty their assigned bus + stops, the live boarding roster, a bulk
boarding update, and a simple live position — reusing the existing driver-side trip data.

**Architecture:** New master tables `Buses`/`BusStops`/`BusAssignments` (migration M0044, RLS), provisioned
out-of-band (no teacher-app create endpoints; tests seed via raw SQL). A new `BusModule` (in the Transport
project) exposes a `/v1/bus` group. Roster/boarding/position **reuse** the existing `Trips`/`Boardings`/
`TripPings` by resolving the bus's current live trip via `Trips.BusNo = Buses.BusNo AND Status='live'`.
Position is computed simply (latest ping → nearest stop); ETA is left null (no speculative model).

**Tech Stack:** .NET 10 minimal APIs, Dapper, FluentMigrator, SQL Server (RLS), ASP.NET authz, xUnit.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-21-teacher-principal-app-complete-design.md` (Phase-4 amendment at top).
- Wire **snake_case**; success `DataEnvelope<T>`; `204` on the boarding POST; errors via
  `Results.Json(ErrorEnvelope.From(new Error(code,msg)), statusCode: N)`. The existing `Forbidden(msg)` helper
  pattern (403) is in `TransportModule.cs` — replicate it in the new file.
- Authz: ALL `/v1/bus` endpoints (GET assigned/roster/position + POST boarding) → `AuthorizationPolicies.TeacherApp`
  (teacher/principal/admin). `student.parent` → 403. `GET /bus/assigned` is additionally self-scoped by the
  authed `UserId` (the teacher's own assignment).
- New tables follow the RLS pattern (`M0021`/`M0040`): GUID PK NewSequentialId, `TenantId` NOT NULL, covering
  index, `rls.<Table>TenantPolicy` FILTER+BLOCK on `rls.fn_tenant_predicate(TenantId)`. Migration **M0044**
  (head is M0043); keep `MigrationIdempotenceTests` green. No insert procs needed (no create endpoints; reads
  are inline; boarding reuses the existing `dbo.Boarding_Upsert`).
- Reuse the existing `dbo.Boarding_Upsert` proc (`@TenantId,@TripId,@StudentId,@StopId,@State,@At`); the
  `Boardings.State` column is unconstrained `nvarchar(10)`, so the new `absent` value is accepted as-is.
- Reads parameterized via `QueryInlineAsync`; RLS scopes by tenant. "now" via `IClock`.
- Known unrelated pre-existing failing test `CatreOpsTests.Onboarding_...checklist` — ignore it.
- Test infra: `.superpowers/sdd/test-infra-cheatsheet.md` — seed `Buses`/`BusStops`/`BusAssignments`/`Trips`/
  `TripPings`/`Boardings` via the raw-SQL `Seed(cs, tenantId, ...)` session-context helper (no create
  endpoints for this data). Mint canonical roles.
- Commit messages conventional; **no** `Co-Authored-By` line.

## Confirmed facts (Transport audit)
- `dbo.Trips(Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status default 'live', StartedAt, EndedAt)`.
- `dbo.TripPings(Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At)` — indexed `(TripId, At)`.
- `dbo.Boardings(Id, TenantId, TripId, StudentId, StopId, State default 'pending', At)` — unique `(TenantId, TripId, StudentId)`.
- `dbo.Boarding_Upsert` proc exists (single record). `dbo.Students(... Name ...)`.
- NO bus/route/stop master, NO teacher↔bus link exist — all new here.
- Transport endpoints live under `/v1/staff` (bare auth); we add a separate `/v1/bus` group.

---

## File Structure
- `db/Sms.Migrations/M0044_Bus_Tables.cs` — **new** (Buses + BusStops + BusAssignments + 3 RLS policies).
- `src/Sms.Modules.Transport/BusModule.cs` — **new** (bus DTOs + `BusRepository` + `AddBusModule`/`MapBusModule`).
- `src/Sms.Api/Program.cs` — **modify** (`AddBusModule()` + `MapBusModule(app)`; `using Sms.Modules.Transport;` already present).
- `src/Sms.Api/Swagger/ApiAudienceMap.cs` + `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs` — **modify** (Task 5).
- Tests: `tests/Sms.Tests.Integration/Transport/Bus{Assigned,Roster,Boarding,Position}Tests.cs` — **new**.

---

### Task 1: Migration M0044 + BusModule skeleton + `GET /v1/bus/assigned`

**Files:** Create `M0044_Bus_Tables.cs`, `BusModule.cs`; modify `Program.cs`; test `Transport/BusAssignedTests.cs`.

**Interfaces:**
- Produces: records `BusStopResponse(Guid Id, string Name, string? Time, int Seq, double Lat, double Lng)`,
  `BusResponse(Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone,
  IReadOnlyList<BusStopResponse> Stops)`; `BusRepository.GetAssignedAsync(Guid teacherUserId, CancellationToken)`
  → `BusResponse?`; `AddBusModule(IServiceCollection)`, `MapBusModule(IEndpointRouteBuilder)`.

- [ ] **Step 1: Migration** `db/Sms.Migrations/M0044_Bus_Tables.cs` (three tables + RLS, pattern of M0040):

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(44, "Bus duty: Buses + BusStops + BusAssignments master tables with tenant RLS")]
public sealed class M0044_Bus_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Buses")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusNo").AsString(40).NotNullable()
            .WithColumn("RouteName").AsString(80).Nullable()
            .WithColumn("Driver").AsString(120).Nullable()
            .WithColumn("DriverPhone").AsString(32).Nullable();
        Create.Index("IX_Buses_Tenant").OnTable("Buses").OnColumn("TenantId").Ascending();

        Create.Table("BusStops")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Time").AsString(10).Nullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0);
        Create.Index("IX_BusStops_Bus").OnTable("BusStops").OnColumn("BusId").Ascending();

        Create.Table("BusAssignments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TeacherUserId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable();
        Create.Index("IX_BusAssignments_Teacher").OnTable("BusAssignments")
            .OnColumn("TenantId").Ascending().OnColumn("TeacherUserId").Ascending().Unique();

        foreach (var t in new[] { "Buses", "BusStops", "BusAssignments" })
            Execute.Sql($@"CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "Buses", "BusStops", "BusAssignments" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("BusAssignments");
        Delete.Table("BusStops");
        Delete.Table("Buses");
    }
}
```

- [ ] **Step 2: BusModule skeleton + first endpoint** — `src/Sms.Modules.Transport/BusModule.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Transport;

public sealed record BusStopResponse(Guid Id, string Name, string? Time, int Seq, double Lat, double Lng);
public sealed record BusResponse(
    Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone, IReadOnlyList<BusStopResponse> Stops);

public sealed class BusRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record BusRow(Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone);

    public async Task<BusResponse?> GetAssignedAsync(Guid teacherUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<BusRow>(
            @"SELECT TOP 1 b.Id, b.BusNo, b.RouteName, b.Driver, b.DriverPhone
              FROM dbo.Buses b JOIN dbo.BusAssignments a ON a.BusId = b.Id
              WHERE a.TeacherUserId = @teacherUserId", new { teacherUserId }, ct)).FirstOrDefault();
        if (bus is null) return null;
        var stops = await QueryInlineAsync<BusStopResponse>(
            "SELECT Id, Name, Time, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq",
            new { busId = bus.Id }, ct);
        return new BusResponse(bus.Id, bus.BusNo, bus.RouteName, bus.Driver, bus.DriverPhone, stops);
    }
}

public static class BusModule
{
    public static IServiceCollection AddBusModule(this IServiceCollection services)
    {
        services.AddScoped<BusRepository>();
        return services;
    }

    internal static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 4: teacher bus-duty view under /v1/bus*. Tenant-scoped; assigned is user-scoped.
    public static IEndpointRouteBuilder MapBusModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/bus").RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapGet("/assigned", async (BusRepository repo, ITenantContext tenant) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            var bus = await repo.GetAssignedAsync(uid);
            return bus is null
                ? Results.Json(ErrorEnvelope.From(new Error("not_found", "no assigned bus")), statusCode: 404)
                : Results.Ok(new DataEnvelope<BusResponse>(bus));
        });

        return app;
    }
}
```

- [ ] **Step 3: Wire Program.cs** — add `builder.Services.AddBusModule();` beside `AddTransportModule()`,
  and `app.MapBusModule();` beside `app.MapTransportModule()`. (`using Sms.Modules.Transport;` already present.)

- [ ] **Step 4: Failing test** `tests/Sms.Tests.Integration/Transport/BusAssignedTests.cs`: mint a teacher
  token for a chosen `userId`; raw-SQL seed (via the cheatsheet `Seed` helper, all rows TenantId = tenant) a
  `Buses` row, two `BusStops` (Seq 1,2), and a `BusAssignments` row (TeacherUserId = that userId, BusId = the
  bus). Assert `GET /v1/bus/assigned` → 200 with `bus_no`, `route_name`, and `stops` (length 2, ordered by
  seq). Assert a teacher with NO assignment → 404. Assert `student.parent` → 403. Run filter → FAIL.

- [ ] **Step 5: Run filter → PASS;** full suite + idempotence check.
- [ ] **Step 6: Commit** — `feat(transport): bus master (M0044) + GET /v1/bus/assigned (teacher)`.

---

### Task 2: `GET /v1/bus/{busId}/roster`

**Files:** modify `BusModule.cs`; test `Transport/BusRosterTests.cs`.

**Interfaces:**
- Produces: record `BusRosterEntry(Guid StudentId, string StudentName, string Initials, Guid? StopId, string Status)`;
  `BusRepository.GetRosterAsync(Guid busId, CancellationToken)` → `IReadOnlyList<BusRosterEntry>` (empty when
  no live trip); a private `CurrentTripIdAsync(Guid busId, CancellationToken)` helper (reused by Tasks 3-4).

- [ ] **Step 1: Repository** — add to `BusRepository` the shared resolver + roster:

```csharp
    // The bus's current live trip, matched by BusNo (Transport trips carry BusNo, not BusId). Null if none live.
    private async Task<Guid?> CurrentTripIdAsync(Guid busId, CancellationToken ct) =>
        (await QueryInlineAsync<Guid>(
            @"SELECT TOP 1 t.Id FROM dbo.Trips t JOIN dbo.Buses b ON b.BusNo = t.BusNo
              WHERE b.Id = @busId AND t.Status = 'live' ORDER BY t.StartedAt DESC",
            new { busId }, ct)).Cast<Guid?>().FirstOrDefault();

    public async Task<IReadOnlyList<BusRosterEntry>> GetRosterAsync(Guid busId, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        if (tripId is null) return [];
        return await QueryInlineAsync<BusRosterEntry>(
            @"SELECT bo.StudentId, s.Name AS StudentName, bo.StopId, bo.State AS Status
              FROM dbo.Boardings bo JOIN dbo.Students s ON s.Id = bo.StudentId
              WHERE bo.TripId = @tripId ORDER BY s.Name", new { tripId }, ct);
    }
```

  NOTE: `Initials` is NOT in the SQL — it is derived in the endpoint projection (the record's `Initials` is
  populated there). To keep Dapper mapping simple, change the SQL select to also feed `Initials`: easiest is
  to map to a private row first. Use this instead — a private row + C# initials:

```csharp
    private sealed record RosterRow(Guid StudentId, string StudentName, Guid? StopId, string Status);

    public async Task<IReadOnlyList<BusRosterEntry>> GetRosterAsync(Guid busId, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        if (tripId is null) return [];
        var rows = await QueryInlineAsync<RosterRow>(
            @"SELECT bo.StudentId, s.Name AS StudentName, bo.StopId, bo.State AS Status
              FROM dbo.Boardings bo JOIN dbo.Students s ON s.Id = bo.StudentId
              WHERE bo.TripId = @tripId ORDER BY s.Name", new { tripId }, ct);
        return rows.Select(r => new BusRosterEntry(r.StudentId, r.StudentName, Initials(r.StudentName), r.StopId, r.Status)).ToList();
    }

    internal static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
```

  And add the record `public sealed record BusRosterEntry(Guid StudentId, string StudentName, string Initials, Guid? StopId, string Status);`
  near the other bus DTOs.

- [ ] **Step 2: Endpoint** — in `MapBusModule`:

```csharp
        g.MapGet("/{busId:guid}/roster", async (Guid busId, BusRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<BusRosterEntry>>(await repo.GetRosterAsync(busId))));
```

- [ ] **Step 3: Failing test** `BusRosterTests.cs`: raw-SQL seed a bus, two students, a LIVE `Trips` row with
  `BusNo` = the bus's BusNo (and Status='live', StartedAt now, DriverId any), and two `Boardings` rows for
  that trip (one `boarded`, one `absent`). Assert `GET /v1/bus/{busId}/roster` (teacher token) → 200 with 2
  entries carrying `student_name`, `initials`, `status` (`boarded`/`absent`). Assert a bus with NO live trip →
  empty list. Assert `student.parent` → 403. Filter → FAIL → implement → PASS.

- [ ] **Step 4: Run filter → PASS;** full suite.
- [ ] **Step 5: Commit** — `feat(transport): GET /v1/bus/{id}/roster (live trip boardings + students)`.

---

### Task 3: `POST /v1/bus/{busId}/boarding`

**Files:** modify `BusModule.cs`; test `Transport/BusBoardingTests.cs`.

**Interfaces:**
- Consumes: `CurrentTripIdAsync` (Task 2), existing `dbo.Boarding_Upsert`, `IClock`.
- Produces: records `BusBoardingItem(Guid StudentId, Guid? StopId, string Status, DateTime? At)`,
  `BusBoardingRequest(IReadOnlyList<BusBoardingItem> Records)`; `BusRepository.UpsertBoardingAsync(Guid busId,
  IReadOnlyList<BusBoardingItem> records, DateTime now, CancellationToken)` → `bool` (false when no live trip).

- [ ] **Step 1: Repository**:

```csharp
    public async Task<bool> UpsertBoardingAsync(
        Guid busId, IReadOnlyList<BusBoardingItem> records, DateTime now, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        if (tripId is null) return false;
        foreach (var r in records)
            await ExecuteProcAsync("dbo.Boarding_Upsert", new
            {
                TenantId = (Guid?)null, // RLS BLOCK uses session context; proc takes @TenantId — pass via tenant below
                TripId = tripId.Value, r.StudentId, r.StopId, State = r.Status, At = r.At ?? now
            }, ct);
        return true;
    }
```

  CORRECTION — `dbo.Boarding_Upsert` requires an explicit `@TenantId` (see `TripRepository.UpsertBoardingAsync`).
  Thread the tenant id in: change the signature to `UpsertBoardingAsync(Guid tenantId, Guid busId, ... )` and
  pass `TenantId = tenantId` in the proc args (drop the null line). The endpoint passes `tenant.TenantId`.

- [ ] **Step 2: DTOs** — add near the other bus DTOs:

```csharp
public sealed record BusBoardingItem(Guid StudentId, Guid? StopId, string Status, DateTime? At);
public sealed record BusBoardingRequest(IReadOnlyList<BusBoardingItem> Records);
```

- [ ] **Step 3: Endpoint** — in `MapBusModule` (add `using Sms.Shared.Kernel.Time;`):

```csharp
        g.MapPost("/{busId:guid}/boarding", async (Guid busId, BusBoardingRequest req, BusRepository repo, ITenantContext tenant, IClock clock) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            var ok = await repo.UpsertBoardingAsync(tid, busId, req.Records, clock.UtcNow);
            return ok ? Results.NoContent()
                      : Results.Json(ErrorEnvelope.From(new Error("no_active_trip", "no live trip for this bus")), statusCode: 409);
        });
```

- [ ] **Step 4: Failing test** `BusBoardingTests.cs`: seed bus + a LIVE trip (BusNo match) + 2 students; POST
  `{ records: [ { student_id, status: "boarded" }, { student_id, status: "absent" } ] }` as a teacher → 204;
  then `GET /v1/bus/{busId}/roster` → both students present with the posted statuses (proves upsert + `absent`
  allowed). POST to a bus with NO live trip → 409 `no_active_trip`. `student.parent` POST → 403. Filter →
  FAIL → implement → PASS.

- [ ] **Step 5: Run filter → PASS;** full suite.
- [ ] **Step 6: Commit** — `feat(transport): POST /v1/bus/{id}/boarding (bulk upsert, absent state)`.

---

### Task 4: `GET /v1/bus/{busId}/position` (simple)

**Files:** modify `BusModule.cs`; test `Transport/BusPositionTests.cs`.

**Interfaces:**
- Consumes: `CurrentTripIdAsync`.
- Produces: record `BusPositionResponse(Guid BusId, int CurrentStopIndex, double Progress, double? Lat,
  double? Lng, string? NextStopName, int? EtaMinutes)`; `BusRepository.GetPositionAsync(Guid busId, CancellationToken)`.

- [ ] **Step 1: Repository** — latest ping → nearest stop; progress; null ETA:

```csharp
    private sealed record StopRow(string Name, int Seq, double Lat, double Lng);
    private sealed record PingRow2(double Lat, double Lng);

    public async Task<BusPositionResponse> GetPositionAsync(Guid busId, CancellationToken ct = default)
    {
        var stops = await QueryInlineAsync<StopRow>(
            "SELECT Name, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq", new { busId }, ct);
        var tripId = await CurrentTripIdAsync(busId, ct);
        PingRow2? ping = tripId is null ? null : (await QueryInlineAsync<PingRow2>(
            "SELECT TOP 1 Lat, Lng FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At DESC",
            new { tripId }, ct)).FirstOrDefault();

        if (ping is null || stops.Count == 0)
            return new BusPositionResponse(busId, 0, 0, ping?.Lat, ping?.Lng, null, null);

        int nearest = 0; double best = double.MaxValue;
        for (int i = 0; i < stops.Count; i++)
        {
            var dist = Haversine(ping.Lat, ping.Lng, stops[i].Lat, stops[i].Lng);
            if (dist < best) { best = dist; nearest = i; }
        }
        double progress = stops.Count > 1 ? Math.Round((double)nearest / (stops.Count - 1), 3) : 0;
        string? next = nearest + 1 < stops.Count ? stops[nearest + 1].Name : null;
        return new BusPositionResponse(busId, nearest, progress, ping.Lat, ping.Lng, next, null);
    }

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2) + Math.Cos(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Sin(dLng/2)*Math.Sin(dLng/2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
```

  And add `public sealed record BusPositionResponse(Guid BusId, int CurrentStopIndex, double Progress, double? Lat, double? Lng, string? NextStopName, int? EtaMinutes);`

- [ ] **Step 2: Endpoint** — in `MapBusModule`:

```csharp
        g.MapGet("/{busId:guid}/position", async (Guid busId, BusRepository repo) =>
            Results.Ok(new DataEnvelope<BusPositionResponse>(await repo.GetPositionAsync(busId))));
```

- [ ] **Step 3: Failing test** `BusPositionTests.cs`: seed bus + 3 stops (Seq 1-3 at known lat/lng) + a LIVE
  trip + a `TripPings` row located nearest to stop index 1 (the middle stop). Assert `GET /v1/bus/{busId}/position`
  (teacher) → `current_stop_index = 1`, `progress = 0.5`, `next_stop_name` = stop 3's name, `lat`/`lng` echo
  the ping, `eta_minutes` null. Assert a bus with NO live trip/ping → `current_stop_index 0`, `progress 0`,
  null lat/lng/next/eta. `student.parent` → 403. Filter → FAIL → implement → PASS.

- [ ] **Step 4: Run filter → PASS;** full suite.
- [ ] **Step 5: Commit** — `feat(transport): GET /v1/bus/{id}/position (nearest-stop, simple progress)`.

---

### Task 5: Swagger audience mapping for `/v1/bus`

**Files:** `src/Sms.Api/Swagger/ApiAudienceMap.cs`; `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs`.

- [ ] **Step 1: Failing assertions** — add to `SwaggerPerAppTests` that the `teacher` doc's paths contain
  `/v1/bus/assigned` and `/v1/bus/{busId}/roster`. Filter `~SwaggerPerAppTests` → FAIL.

- [ ] **Step 2: Map** — in `ApiAudienceMap.cs` `Rules`, add `("v1/bus", [Teacher])` among the school-scoped
  rules. (Note `v1/staff/trips` already exists for the Staff app and does not collide — bus is its own prefix.)

- [ ] **Step 3: Run filter → PASS.**
- [ ] **Step 4: Commit** — `feat(api): expose /v1/bus in the Teacher Swagger doc`.

---

## Self-Review

- **Spec coverage:** §5.5 (amendment): `Buses`/`BusStops`/`BusAssignments` (Task 1, M0044), `GET /bus/assigned`
  (Task 1, user-scoped), `GET /bus/{id}/roster` (Task 2, reuses live trip + Students join), `POST /bus/{id}/boarding`
  (Task 3, reuses `Boarding_Upsert`, `absent` state, 409 when no live trip), `GET /bus/{id}/position` (Task 4,
  simple nearest-stop, null ETA), Swagger (Task 5). All `/v1/bus` gated `TeacherApp`; `student.parent` → 403 in
  each test. No create endpoints for bus master (provisioned out-of-band; tests seed raw SQL).
- **Placeholder scan:** none — migration, module, repo methods, endpoints, and key tests are complete. Task 2's
  Step 1 shows the corrected (private-row + C# initials) version; Task 3's Step 1 carries an inline CORRECTION
  to thread `tenantId` into `Boarding_Upsert` — the implementer must use the corrected signature
  `UpsertBoardingAsync(Guid tenantId, Guid busId, ...)`.
- **Type consistency:** `BusResponse`/`BusStopResponse`/`GetAssignedAsync`; `BusRosterEntry`/`GetRosterAsync` +
  `CurrentTripIdAsync` + `Initials`; `BusBoardingItem`/`BusBoardingRequest`/`UpsertBoardingAsync(tenantId,busId,...)`;
  `BusPositionResponse`/`GetPositionAsync` + private `Haversine`. `AddBusModule`/`MapBusModule` wired in Program.cs.

## Next phase
- **Phase 5** — final Swagger/test sweep + full integration run + the deferred follow-ups (check-in query
  pushdown, exam-paper GET/POST gating, the Student-doc audience reconciliation, onboarding test triage).
