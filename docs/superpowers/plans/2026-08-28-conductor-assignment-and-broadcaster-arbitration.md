# Conductor Assignment + Single-Broadcaster Arbitration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a conductor be assigned to a bus, legally operate that bus's trips, and have the
system compute which of driver/conductor is the currently-active GPS broadcaster (driver
preferred), so the conductor's app can auto-start its own broadcast only when needed.

**Architecture:** Backend: mirror the existing driver-assignment pattern (`Buses.DriverStaffId`)
for conductors, generalize the existing driver-only trip-ownership check to admit either role, and
add ping-freshness tracking with a pure, unit-tested function that computes the active broadcaster.
Frontend: replace the unused `broadcasterId` field with the new `activeBroadcaster` contract and
drive the conductor's `TripScreen` broadcast start/stop off it via a small pure decision function,
polling `trip/current` while a trip is live.

**Tech Stack:** .NET 10 / ASP.NET Core / Dapper / SQL Server (FluentMigrator) for `sms-backend`;
Expo/React Native / TypeScript / TanStack Query / Jest for `sms-staff`.

**Spec:** `docs/superpowers/specs/2026-08-28-conductor-assignment-and-broadcaster-arbitration-design.md`

## Global Constraints

- Backend changes touch ONLY Transport-related files (`Sms.Modules.Transport`, `Sms.Application/Services/Transport`, `Sms.Api/Controllers/TripController.cs` and `TransportController.cs`, `Sms.Shared.Kernel/Authz/RoleChecks.cs`, and new/edited files under `db/Sms.Migrations`). Do not touch any other in-flight uncommitted files in this repo.
- Migration numbering: before creating the migration in Task 1, run `ls db/Sms.Migrations/*.cs | grep -oE "M[0-9]{4}" | sort | tail -1` to find the current highest number N, and name the new migration `M{N+1}_...` — this repo has concurrent activity, so the number may have moved past what's written here.
- This backend repo may have a live `dotnet watch`-style process running (check for locked `bin/` DLLs before assuming a build failure is your own code). Never kill a process you didn't start to "fix" a build lock — report it instead.
- STALE threshold for broadcaster arbitration is 30 seconds (matches the existing 10s ping cadence with margin for one missed cycle) — use this exact value, don't invent a different one.
- No admin UI changes — this plan only adds the schema/endpoint parameters, per the spec's explicit non-goal.
- No changes to `FleetBusResponse`, `BusService.GetFleetAsync`, or any admin fleet-view code — out of scope.

---

## Task 1: Conductor↔bus linking (schema, admin endpoint plumbing, Trip_Start, assignment name)

**Files:**
- Create: `db/Sms.Migrations/M0154_Buses_ConductorStaffId.cs` (verify number per Global Constraints)
- Modify: `db/Sms.Migrations/procs/transport/Trip_Start.sql`
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` (`BusRepository.CreateBusAsync`/`UpdateBusAsync`, `CreatedBusRow`, `UpdatedBusRow`, `TripRepository.GetAssignmentAsync`)
- Modify: `src/Sms.Application/Services/Transport/BusService.cs` (`IBusService.CreateBusAsync`/`UpdateBusAsync`, implementation)
- Modify: `src/Sms.Api/Controllers/TransportController.cs` (`CreateBusRequest`, `UpdateBusRequest`, `CreateBus`, `UpdateBus`)
- Test: `tests/Sms.Tests.Integration/Transport/BusConductorAssignmentTests.cs` (new)
- Test: `tests/Sms.Tests.Integration/Transport/StaffTripAssignmentTests.cs` (extend)

**Interfaces:**
- Produces: `Buses.ConductorStaffId` column; `IBusService.CreateBusAsync(..., Guid? conductorStaffId = null, ...)` and `UpdateBusAsync(..., Guid? conductorStaffId = null, bool clearConductor = false, ...)`; `Trips.ConductorId` is now populated by `Trip_Start`; `TripRepository.GetAssignmentAsync` returns a real `ConductorName` instead of always `null`.
- Consumes: nothing from other tasks (this is the foundation task).

- [ ] **Step 1: Find the current migration number**

Run: `ls db/Sms.Migrations/*.cs | grep -oE "M[0-9]{4}" | sort | tail -1`

Note the number `N`. The new migration in this task is `M{N+1}`. The examples below use `M0154` —
replace with your actual number throughout this task if different.

- [ ] **Step 2: Write the migration**

Create `db/Sms.Migrations/M0154_Buses_ConductorStaffId.cs`:

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(154, "Transport: Buses.ConductorStaffId + conductor-aware Bus_Update/Bus_Create + Trip_Start auto-assigns ConductorId")]
public sealed class M0154_Buses_ConductorStaffId : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'ConductorStaffId') IS NULL
    ALTER TABLE dbo.Buses ADD ConductorStaffId uniqueidentifier NULL;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0,
    @ConductorStaffId uniqueidentifier = NULL,
    @ClearConductor bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;
    IF @ClearConductor = 1 SET @ConductorStaffId = NULL;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId AND Id <> @BusId;

        UPDATE b SET
            b.DriverStaffId = @DriverStaffId,
            b.Driver = s.Name,
            b.DriverPhone = s.Phone
        FROM dbo.Buses b
        INNER JOIN dbo.Staff s ON s.Id = @DriverStaffId AND s.TenantId = @TenantId
        WHERE b.Id = @BusId;
    END
    ELSE IF @ClearDriver = 1
        UPDATE dbo.Buses SET DriverStaffId = NULL, Driver = NULL, DriverPhone = NULL WHERE Id = @BusId;

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId AND Id <> @BusId;

        UPDATE dbo.Buses SET ConductorStaffId = @ConductorStaffId WHERE Id = @BusId AND TenantId = @TenantId;
    END
    ELSE IF @ClearConductor = 1
        UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @BusId;

    UPDATE b SET
        b.BusNo = COALESCE(@BusNo, b.BusNo),
        b.RouteId = CASE WHEN @RouteId IS NOT NULL THEN @RouteId ELSE b.RouteId END,
        b.RouteName = CASE WHEN @RouteId IS NOT NULL THEN r.Name ELSE b.RouteName END
    FROM dbo.Buses b
    LEFT JOIN dbo.TransportRoutes r ON r.Id = @RouteId AND r.TenantId = @TenantId
    WHERE b.Id = @BusId AND b.TenantId = @TenantId;

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        b.ConductorStaffId,
        CASE WHEN b.RouteId IS NOT NULL
            THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId)
            ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END AS StopCount,
        (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned
    FROM dbo.Buses b WHERE b.Id = @BusId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ConductorStaffId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @ResolvedRouteId uniqueidentifier = @RouteId;

    IF @ResolvedRouteId IS NULL AND @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @ResolvedRouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId;

        SELECT @Driver = s.Name, @DriverPhone = s.Phone
        FROM dbo.Staff s WHERE s.Id = @DriverStaffId AND s.TenantId = @TenantId;
    END

    IF @ConductorStaffId IS NOT NULL
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId;

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId, ConductorStaffId)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId, @ConductorStaffId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        b.ConductorStaffId,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport.Trip_Start"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'ConductorStaffId') IS NOT NULL
    ALTER TABLE dbo.Buses DROP COLUMN ConductorStaffId;");
    }
}
```

Note: `Down()` intentionally does not revert `Bus_Update`/`Bus_Create`/`Trip_Start` back to their
prior SQL text — this matches the existing convention in `M0077_Trips_BusId.cs`, which only drops
its added column/index and leaves the redeployed proc as-is.

- [ ] **Step 3: Update `Trip_Start.sql` to auto-resolve the conductor**

Replace the full contents of `db/Sms.Migrations/procs/transport/Trip_Start.sql` with:

```sql
CREATE OR ALTER PROCEDURE dbo.Trip_Start
    @TenantId uniqueidentifier, @RouteId uniqueidentifier, @BusNo nvarchar(40),
    @DriverId uniqueidentifier, @Direction nvarchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();

    -- Bind the trip to a concrete BusId resolved WITHIN THIS TENANT. Never trust the
    -- bus number alone: a bus number can repeat across schools, so we scope the lookup
    -- by @TenantId (belt-and-suspenders on top of RLS) so a trip can only ever point at
    -- this tenant's bus. Live tracking then matches on BusId, not the number.
    DECLARE @BusId uniqueidentifier =
        (SELECT TOP 1 Id FROM dbo.Buses
         WHERE TenantId = @TenantId AND BusNo = @BusNo ORDER BY Id);

    -- Auto-assign the trip's conductor from the bus's ConductorStaffId, resolved to their
    -- login identity (Staff.UserId) — ConductorId is a user id, same as DriverId, not a
    -- Staff.Id, so trip-ownership checks can compare it directly against the caller's uid.
    DECLARE @ConductorId uniqueidentifier =
        (SELECT s.UserId FROM dbo.Buses b
         JOIN dbo.Staff s ON s.Id = b.ConductorStaffId
         WHERE b.Id = @BusId);

    INSERT dbo.Trips (Id, TenantId, RouteId, BusId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt)
    VALUES (@Id, @TenantId, @RouteId, @BusId, @BusNo, @DriverId, @ConductorId, ISNULL(@Direction, 'pickup'), 'live', SYSUTCDATETIME());

    SELECT Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt
    FROM dbo.Trips WHERE Id = @Id;
END
```

- [ ] **Step 4: Extend `CreatedBusRow`/`UpdatedBusRow` and repository methods**

In `src/Sms.Modules.Transport/TransportModule.cs`, change:

```csharp
public sealed record CreatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, int StudentsRiding, string Status);
public sealed record UpdatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone, int StopCount, int StudentsAssigned);
```

to:

```csharp
public sealed record CreatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, int StudentsRiding, string Status, Guid? ConductorStaffId = null);
public sealed record UpdatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone, int StopCount, int StudentsAssigned, Guid? ConductorStaffId = null);
```

Then change `BusRepository.CreateBusAsync` and `UpdateBusAsync`:

```csharp
public async Task<CreatedBusRow?> CreateBusAsync(
    Guid tenantId, string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone,
    Guid? driverStaffId, Guid? conductorStaffId = null, CancellationToken ct = default) =>
    await QuerySingleProcAsync<CreatedBusRow>("dbo.Bus_Create",
        new
        {
            TenantId = tenantId, BusNo = busNo, RouteName = routeName, RouteId = routeId,
            Driver = driver, DriverPhone = driverPhone, DriverStaffId = driverStaffId,
            ConductorStaffId = conductorStaffId
        }, ct);

public async Task<UpdatedBusRow?> UpdateBusAsync(
    Guid tenantId, Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
    Guid? conductorStaffId = null, bool clearConductor = false,
    CancellationToken ct = default) =>
    await QuerySingleProcAsync<UpdatedBusRow>("dbo.Bus_Update",
        new
        {
            TenantId = tenantId, BusId = busId, BusNo = busNo, RouteId = routeId,
            DriverStaffId = driverStaffId, ClearDriver = clearDriver,
            ConductorStaffId = conductorStaffId, ClearConductor = clearConductor
        }, ct);
```

- [ ] **Step 5: Resolve `ConductorName` in `GetAssignmentAsync`**

In the same file, change `TripRepository.GetAssignmentAsync` from:

```csharp
    public async Task<StaffTripAssignmentResponse?> GetAssignmentAsync(Guid driverUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<AssignedBusRow>(
            @"SELECT b.BusNo, b.RouteId FROM dbo.Buses b
              JOIN dbo.Staff s ON s.Id = b.DriverStaffId
              WHERE s.UserId = @driverUserId", new { driverUserId }, ct)).FirstOrDefault();
        if (bus?.RouteId is not { } routeId) return null;

        var route = (await QueryInlineAsync<RouteRow>(
            "SELECT Id, Name FROM dbo.TransportRoutes WHERE Id = @routeId", new { routeId }, ct)).FirstOrDefault();
        if (route is null) return null;

        var stops = await QueryInlineAsync<StaffStopResponse>(
            "SELECT Id, Name, Lat, Lng, Seq, CAST(NULL AS int) AS EtaMin FROM dbo.RouteStops WHERE RouteId = @routeId ORDER BY Seq",
            new { routeId }, ct);

        return new StaffTripAssignmentResponse(new StaffRouteResponse(route.Id, route.Name, bus.BusNo, stops), bus.BusNo, null);
    }
```

to (adds `ConductorName` to the row and a left join, replacing the hardcoded `null`):

```csharp
    public async Task<StaffTripAssignmentResponse?> GetAssignmentAsync(Guid driverUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<AssignedBusRow>(
            @"SELECT b.BusNo, b.RouteId, cs.Name AS ConductorName
              FROM dbo.Buses b
              JOIN dbo.Staff s ON s.Id = b.DriverStaffId
              LEFT JOIN dbo.Staff cs ON cs.Id = b.ConductorStaffId
              WHERE s.UserId = @driverUserId", new { driverUserId }, ct)).FirstOrDefault();
        if (bus?.RouteId is not { } routeId) return null;

        var route = (await QueryInlineAsync<RouteRow>(
            "SELECT Id, Name FROM dbo.TransportRoutes WHERE Id = @routeId", new { routeId }, ct)).FirstOrDefault();
        if (route is null) return null;

        var stops = await QueryInlineAsync<StaffStopResponse>(
            "SELECT Id, Name, Lat, Lng, Seq, CAST(NULL AS int) AS EtaMin FROM dbo.RouteStops WHERE RouteId = @routeId ORDER BY Seq",
            new { routeId }, ct);

        return new StaffTripAssignmentResponse(
            new StaffRouteResponse(route.Id, route.Name, bus.BusNo, stops), bus.BusNo, bus.ConductorName);
    }
```

And change the private `AssignedBusRow` record just above `GetAssignmentAsync` from
`private sealed record AssignedBusRow(string BusNo, Guid? RouteId);` to
`private sealed record AssignedBusRow(string BusNo, Guid? RouteId, string? ConductorName);`.

- [ ] **Step 6: Thread `conductorStaffId`/`clearConductor` through `IBusService`/`BusService`**

In `src/Sms.Application/Services/Transport/BusService.cs`, change the interface methods:

```csharp
    Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        CancellationToken ct = default);
```
to
```csharp
    Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        Guid? conductorStaffId = null, CancellationToken ct = default);
```
and
```csharp
    Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        CancellationToken ct = default);
```
to
```csharp
    Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        Guid? conductorStaffId = null, bool clearConductor = false, CancellationToken ct = default);
```

Then in the class body, change `CreateBusAsync`'s signature and body from:

```csharp
    public async Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<FleetBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<FleetBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var trimmed = busNo.Trim();
        if (trimmed.Length == 0)
            return ApiResult<FleetBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        var row = await repo.CreateBusAsync(tid, trimmed, routeName?.Trim(), routeId, driver?.Trim(), driverPhone?.Trim(), driverStaffId, ct);
        if (row is null)
            return ApiResult<FleetBusResponse>.Fail(new Error("server_error", "could not create bus"), 500);
        return ApiResult<FleetBusResponse>.Ok(ToFleetBus(row), 201);
    }
```

to:

```csharp
    public async Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        Guid? conductorStaffId = null, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<FleetBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<FleetBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var trimmed = busNo.Trim();
        if (trimmed.Length == 0)
            return ApiResult<FleetBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        if (conductorStaffId is Guid cid && !await repo.StaffExistsAsync(cid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "conductor staff not found"), 404);
        var row = await repo.CreateBusAsync(tid, trimmed, routeName?.Trim(), routeId, driver?.Trim(), driverPhone?.Trim(), driverStaffId, conductorStaffId, ct);
        if (row is null)
            return ApiResult<FleetBusResponse>.Fail(new Error("server_error", "could not create bus"), 500);
        return ApiResult<FleetBusResponse>.Ok(ToFleetBus(row), 201);
    }
```

And change `UpdateBusAsync` from:

```csharp
    public async Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<TransportBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<TransportBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        var trimmed = busNo?.Trim();
        if (trimmed is { Length: 0 })
            return ApiResult<TransportBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        var row = await repo.UpdateBusAsync(tid, busId, trimmed, routeId, driverStaffId, clearDriver, ct);
        if (row is null)
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        return ApiResult<TransportBusResponse>.Ok(new TransportBusResponse(
            row.BusId, row.BusNo, row.RouteId, row.RouteName, row.DriverStaffId, row.Driver, row.DriverPhone,
            row.StopCount, row.StudentsAssigned, null, null));
    }
```

to:

```csharp
    public async Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        Guid? conductorStaffId = null, bool clearConductor = false, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<TransportBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<TransportBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        if (conductorStaffId is Guid cid && !await repo.StaffExistsAsync(cid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "conductor staff not found"), 404);
        var trimmed = busNo?.Trim();
        if (trimmed is { Length: 0 })
            return ApiResult<TransportBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        var row = await repo.UpdateBusAsync(tid, busId, trimmed, routeId, driverStaffId, clearDriver, conductorStaffId, clearConductor, ct);
        if (row is null)
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        return ApiResult<TransportBusResponse>.Ok(new TransportBusResponse(
            row.BusId, row.BusNo, row.RouteId, row.RouteName, row.DriverStaffId, row.Driver, row.DriverPhone,
            row.StopCount, row.StudentsAssigned, null, null, row.ConductorStaffId));
    }
```

Finally, in `src/Sms.Modules.Transport/TransportModule.cs`, change `TransportBusResponse` from:

```csharp
public sealed record TransportBusResponse(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone,
    int StopCount, int StudentsAssigned, Guid? TeacherUserId, string? TeacherName);
```

to:

```csharp
public sealed record TransportBusResponse(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone,
    int StopCount, int StudentsAssigned, Guid? TeacherUserId, string? TeacherName,
    Guid? ConductorStaffId = null);
```

- [ ] **Step 7: Thread `ConductorStaffId`/`ClearConductor` through `TransportController`**

In `src/Sms.Api/Controllers/TransportController.cs`, change:

```csharp
public sealed record CreateBusRequest(
    string BusNo, string? RouteName, Guid? RouteId, string? Driver, string? DriverPhone, Guid? DriverStaffId);

public sealed record UpdateBusRequest(
    string? BusNo, Guid? RouteId, Guid? DriverStaffId, bool ClearDriver = false);
```

to:

```csharp
public sealed record CreateBusRequest(
    string BusNo, string? RouteName, Guid? RouteId, string? Driver, string? DriverPhone, Guid? DriverStaffId,
    Guid? ConductorStaffId = null);

public sealed record UpdateBusRequest(
    string? BusNo, Guid? RouteId, Guid? DriverStaffId, bool ClearDriver = false,
    Guid? ConductorStaffId = null, bool ClearConductor = false);
```

And change the two controller actions:

```csharp
    [HttpPost("buses")]
    public async Task<IActionResult> CreateBus([FromBody] CreateBusRequest req, CancellationToken ct) =>
        FromResult(await bus.CreateBusAsync(req.BusNo, req.RouteName, req.RouteId, req.Driver, req.DriverPhone, req.DriverStaffId));

    [HttpPut("buses/{busId:guid}")]
    public async Task<IActionResult> UpdateBus(
        Guid busId, [FromBody] UpdateBusRequest req, CancellationToken ct) =>
        FromResult(await bus.UpdateBusAsync(busId, req.BusNo, req.RouteId, req.DriverStaffId, req.ClearDriver, ct));
```

to:

```csharp
    [HttpPost("buses")]
    public async Task<IActionResult> CreateBus([FromBody] CreateBusRequest req, CancellationToken ct) =>
        FromResult(await bus.CreateBusAsync(req.BusNo, req.RouteName, req.RouteId, req.Driver, req.DriverPhone, req.DriverStaffId, req.ConductorStaffId, ct));

    [HttpPut("buses/{busId:guid}")]
    public async Task<IActionResult> UpdateBus(
        Guid busId, [FromBody] UpdateBusRequest req, CancellationToken ct) =>
        FromResult(await bus.UpdateBusAsync(busId, req.BusNo, req.RouteId, req.DriverStaffId, req.ClearDriver, req.ConductorStaffId, req.ClearConductor, ct));
```

- [ ] **Step 8: Write the integration tests (mirrors `BusAssignedTests.cs`'s style)**

Create `tests/Sms.Tests.Integration/Transport/BusConductorAssignmentTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class BusConductorAssignmentTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient PrincipalClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, [Policies.Principal], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static HttpClient DriverClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, ["driver"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task Seed(string cs, Guid tenantId, Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await work(conn);
    }

    [Fact]
    public async Task UpdateBus_assigns_a_conductor_and_starting_a_trip_sets_ConductorId()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var conductorStaffId = Guid.NewGuid();
        var conductorUserId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, @BusNo)",
                new { Id = busId, TenantId = tenantId, BusNo = busNo });
            await conn.ExecuteAsync(
                "INSERT dbo.Staff (Id, TenantId, Name, UserId) VALUES (@Id, @TenantId, @Name, @UserId)",
                new { Id = conductorStaffId, TenantId = tenantId, Name = "Priya Rao", UserId = conductorUserId });
        });

        var admin = PrincipalClient(app, tenantId);
        var updated = await Data(await admin.PutAsJsonAsync($"/v1/transport/buses/{busId}",
            new { conductor_staff_id = conductorStaffId }), HttpStatusCode.OK);
        updated.GetProperty("conductor_staff_id").GetGuid().Should().Be(conductorStaffId);

        var driver = DriverClient(app, tenantId, Guid.NewGuid());
        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = busNo }), HttpStatusCode.Created);
        trip.GetProperty("conductor_id").GetGuid().Should().Be(conductorUserId);
    }

    [Fact]
    public async Task GetAssignment_returns_the_conductor_name_for_the_driver()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var driverStaffId = Guid.NewGuid();
        var conductorStaffId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Staff (Id, TenantId, Name, UserId) VALUES (@Id, @TenantId, @Name, @UserId)",
                new[]
                {
                    new { Id = driverStaffId, TenantId = tenantId, Name = "Ram Kumar", UserId = (Guid?)driverUserId },
                    new { Id = conductorStaffId, TenantId = tenantId, Name = "Priya Rao", UserId = (Guid?)null },
                });
            await conn.ExecuteAsync(
                "INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@Id, @TenantId, @Name)",
                new { Id = routeId, TenantId = tenantId, Name = "North Route" });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo, RouteId, DriverStaffId, ConductorStaffId) VALUES (@Id, @TenantId, @BusNo, @RouteId, @DriverStaffId, @ConductorStaffId)",
                new { Id = busId, TenantId = tenantId, BusNo = busNo, RouteId = routeId, DriverStaffId = driverStaffId, ConductorStaffId = conductorStaffId });
        });

        var driver = DriverClient(app, tenantId, driverUserId);
        var data = await Data(await driver.GetAsync("/v1/staff/trip/assignment"), HttpStatusCode.OK);
        data.GetProperty("conductor_name").GetString().Should().Be("Priya Rao");
    }
}
```

- [ ] **Step 9: Build to verify (integration DB run may be blocked — see Global Constraints)**

Run: `dotnet build src/Sms.Api` from the `sms-backend` root.
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or, if a live process locks the output, `dotnet build src/Sms.Modules.Transport` and `dotnet build src/Sms.Application` individually, which don't need that lock).

If a SQL Server instance is reachable and no live process is locking the build, additionally run:
`dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~BusConductorAssignmentTests"`
Expected: both tests pass. If this can't run, note why in your task summary rather than guessing.

- [ ] **Step 10: Commit**

```bash
git add db/Sms.Migrations/M0154_Buses_ConductorStaffId.cs db/Sms.Migrations/procs/transport/Trip_Start.sql src/Sms.Modules.Transport/TransportModule.cs src/Sms.Application/Services/Transport/BusService.cs src/Sms.Api/Controllers/TransportController.cs tests/Sms.Tests.Integration/Transport/BusConductorAssignmentTests.cs tests/Sms.Tests.Integration/Transport/StaffTripAssignmentTests.cs
git commit -m "feat(transport): link a conductor to a bus, auto-assign Trips.ConductorId, resolve conductor_name"
```

---

## Task 2: Conductor can legally operate a trip

**Files:**
- Modify: `src/Sms.Shared.Kernel/Authz/RoleChecks.cs`
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` (`IsOwnedByDriverAsync` → `GetParticipantRoleAsync`, `GetCurrentAsync`)
- Modify: `src/Sms.Application/Services/Transport/TripService.cs` (all call sites)
- Test: `tests/Sms.Tests.Integration/Transport/TripOwnershipTests.cs` (extend)

**Interfaces:**
- Consumes: `Trips.ConductorId` populated by Task 1's `Trip_Start`.
- Produces: `TripRepository.GetParticipantRoleAsync(Guid tripId, Guid userId, CancellationToken ct = default) : Task<string?>` returning `"driver"`, `"conductor"`, or `null` — Task 3 also consumes this to decide which `LastPingAt` column to update.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Sms.Tests.Integration/Transport/TripOwnershipTests.cs` (two new `[Fact]` methods in the
existing class — keep the existing `Peer_driver_in_same_tenant_cannot_mutate_anothers_trip` test as-is):

```csharp
    private static HttpClient ConductorClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, ["conductor"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Assigned_conductor_can_ping_board_and_end_the_trip()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var conductorUserId = Guid.NewGuid();
        var conductorStaffId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Staff (Id, TenantId, Name, UserId) VALUES (@Id, @TenantId, @Name, @UserId)",
                new { Id = conductorStaffId, TenantId = tenantId, Name = "Priya Rao", UserId = conductorUserId });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo, ConductorStaffId) VALUES (@Id, @TenantId, @BusNo, @ConductorStaffId)",
                new { Id = busId, TenantId = tenantId, BusNo = busNo, ConductorStaffId = conductorStaffId });
        }

        var driver = StaffClient(app, tenantId, Guid.NewGuid());
        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = busNo }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        var conductor = ConductorClient(app, tenantId, conductorUserId);
        var now = DateTime.UtcNow;
        (await conductor.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 12.9, lng = 77.5, speed_kmh = 20, heading = 10, at = now } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await conductor.PostAsJsonAsync($"/v1/staff/trips/{tripId}/boarding",
            new { student_id = Guid.NewGuid(), state = "boarded", at = now }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await conductor.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Peer_conductor_not_assigned_to_the_trip_cannot_mutate_it()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var driver = StaffClient(app, tenantId, Guid.NewGuid());
        var peerConductor = ConductorClient(app, tenantId, Guid.NewGuid());

        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = "KA-01-F-7701" }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        (await peerConductor.PostAsync($"/v1/staff/trips/{tripId}/end", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
```

Add `using Dapper;` to the top of the file if not already present (it isn't, per the current file).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~TripOwnershipTests"`
Expected: the two new tests FAIL — `Assigned_conductor_can_ping_board_and_end_the_trip` with 403
(conductor claim not admitted by `CanOperateTrips`/`RoleChecks`, or admitted but rejected by
`IsOwnedByDriverAsync` since it only checks `DriverId`), `Peer_conductor_not_assigned_to_the_trip_cannot_mutate_it`
may already incidentally pass (still worth having, to lock in the behavior once the role is admitted).
If a live process/DB blocks this run, verify by `dotnet build tests/Sms.Tests.Integration` only, and
note the DB run couldn't happen.

- [ ] **Step 3: Admit conductor claims in `RoleChecks.CanOperateTrips`**

In `src/Sms.Shared.Kernel/Authz/RoleChecks.cs`, change:

```csharp
    /// Drivers (and staff) may start/own live trips. Parents and students may not.
    public static bool CanOperateTrips(ClaimsPrincipal user)
    {
        if (IsStaff(user)) return true;
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role == "driver" || role.Contains("driver"))
                return true;
        }
        return false;
    }
```

to:

```csharp
    /// Drivers, conductors, and staff may start/own live trips. Parents and students may not.
    public static bool CanOperateTrips(ClaimsPrincipal user)
    {
        if (IsStaff(user)) return true;
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role == "driver" || role.Contains("driver") || role == "conductor" || role.Contains("conductor"))
                return true;
        }
        return false;
    }
```

- [ ] **Step 4: Replace `IsOwnedByDriverAsync` with `GetParticipantRoleAsync`**

In `src/Sms.Modules.Transport/TransportModule.cs`, change:

```csharp
    /// True when the trip exists in the caller's tenant (RLS) AND is owned by this driver.
    /// Guards driver-app mutations against acting on a peer's trip within the same school.
    public async Task<bool> IsOwnedByDriverAsync(Guid tripId, Guid driverId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>(
            "SELECT COUNT(1) FROM dbo.Trips WHERE Id = @tripId AND DriverId = @driverId",
            new { tripId, driverId }, ct)).First() > 0;
```

to:

```csharp
    private sealed record TripParticipantsRow(Guid? DriverId, Guid? ConductorId);

    /// Returns "driver", "conductor", or null if the caller is neither — the trip's driver or
    /// its assigned conductor may operate it, RLS already scopes the row to the caller's tenant.
    /// Guards driver-app mutations against acting on a peer's trip within the same school.
    public async Task<string?> GetParticipantRoleAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        var row = (await QueryInlineAsync<TripParticipantsRow>(
            "SELECT DriverId, ConductorId FROM dbo.Trips WHERE Id = @tripId",
            new { tripId }, ct)).FirstOrDefault();
        if (row is null) return null;
        if (row.DriverId == userId) return "driver";
        if (row.ConductorId == userId) return "conductor";
        return null;
    }
```

Also change `GetCurrentAsync` — it currently only matches `DriverId`, so a conductor's own
`/staff/trip/current` poll would never see their live trip:

```csharp
    public async Task<TripResponse?> GetCurrentAsync(Guid driverId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TripResponse>(
            $"SELECT TOP 1 {TripCols} FROM dbo.Trips WHERE DriverId = @driverId AND Status = 'live' ORDER BY StartedAt DESC",
            new { driverId }, ct)).FirstOrDefault();
```

to:

```csharp
    public async Task<TripResponse?> GetCurrentAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TripResponse>(
            $"SELECT TOP 1 {TripCols} FROM dbo.Trips WHERE (DriverId = @userId OR ConductorId = @userId) AND Status = 'live' ORDER BY StartedAt DESC",
            new { userId }, ct)).FirstOrDefault();
```

- [ ] **Step 5: Update every `TripService` call site**

In `src/Sms.Application/Services/Transport/TripService.cs`, in `IngestPingsAsync`, `EndAsync`,
`ListBoardingAsync`, `UpsertBoardingAsync`, and `GetRosterAsync`, replace every occurrence of:

```csharp
        if (!await repo.IsOwnedByDriverAsync(tripId, uid, ct))
            return ApiResult<...>.Fail(new Error("forbidden", "not your trip"), 403);
```

with:

```csharp
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<...>.Fail(new Error("forbidden", "not your trip"), 403);
```

(keep each method's own `ApiResult<...>`/`ApiResult` return type as it already is — only the guard
condition changes). Task 3 will revisit `IngestPingsAsync` again to also capture the returned role
string instead of discarding it.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~TripOwnershipTests|FullyQualifiedName~TransportTripsTests|FullyQualifiedName~TripBroadcastTests|FullyQualifiedName~StaffTripAssignmentTests"`
Expected: all pass, including the pre-existing tests in those files (regression check — a peer
driver must still be 403'd, per `Peer_driver_in_same_tenant_cannot_mutate_anothers_trip`).

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build src/Sms.Api`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Shared.Kernel/Authz/RoleChecks.cs src/Sms.Modules.Transport/TransportModule.cs src/Sms.Application/Services/Transport/TripService.cs tests/Sms.Tests.Integration/Transport/TripOwnershipTests.cs
git commit -m "feat(transport): let the assigned conductor legally operate a trip, fix conductor's trip/current lookup"
```

---

## Task 3: Broadcaster arbitration (ping freshness + `ActiveBroadcaster`)

**Files:**
- Create: `src/Sms.Application/Services/Transport/TripBroadcasterRules.cs`
- Test: `tests/Sms.Tests.Unit/Transport/TripBroadcasterRulesTests.cs` (new folder)
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` (`TripResponse`, `TripCols`, new `MarkPingAsync`)
- Modify: `src/Sms.Application/Services/Transport/TripService.cs` (inject `IClock`, wire arbitration)
- Test: `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs` (extend)

**Interfaces:**
- Consumes: `TripRepository.GetParticipantRoleAsync` from Task 2 (to know which role is pinging).
- Produces: `TripBroadcasterRules.Compute(DateTime? driverLastPingAt, DateTime? conductorLastPingAt, DateTime now) : string?` (pure, `"driver"`/`"conductor"`/`null`) — this is the function Task 4's frontend contract (`active_broadcaster` JSON field) mirrors conceptually; `TripResponse.ActiveBroadcaster` is the outward field.

- [ ] **Step 1: Write the failing unit tests (pure function, no DB)**

Create `tests/Sms.Tests.Unit/Transport/TripBroadcasterRulesTests.cs`:

```csharp
using Sms.Application.Services.Transport;

namespace Sms.Tests.Unit.Transport;

public class TripBroadcasterRulesTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Driver_wins_when_only_driver_has_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-5), null, Now);
        Assert.Equal("driver", result);
    }

    [Fact]
    public void Conductor_wins_when_only_conductor_has_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(null, Now.AddSeconds(-5), Now);
        Assert.Equal("conductor", result);
    }

    [Fact]
    public void Driver_is_preferred_when_both_have_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-5), Now.AddSeconds(-1), Now);
        Assert.Equal("driver", result);
    }

    [Fact]
    public void Conductor_takes_over_once_the_drivers_ping_goes_stale()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-31), Now.AddSeconds(-5), Now);
        Assert.Equal("conductor", result);
    }

    [Fact]
    public void Returns_null_when_neither_has_pinged_yet()
    {
        var result = TripBroadcasterRules.Compute(null, null, Now);
        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_both_are_stale()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-40), Now.AddSeconds(-35), Now);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter "FullyQualifiedName~TripBroadcasterRulesTests"`
Expected: FAIL with a compile error (`TripBroadcasterRules` doesn't exist yet) — that's the
expected "fails for the right reason" signal here since the type doesn't exist.

- [ ] **Step 3: Implement `TripBroadcasterRules`**

Create `src/Sms.Application/Services/Transport/TripBroadcasterRules.cs`:

```csharp
namespace Sms.Application.Services.Transport;

/// Decides which role (driver or conductor) is treated as the trip's active GPS broadcaster,
/// purely from ping recency — driver preferred. This is display/decision-side only: the server
/// never rejects a ping based on this (see the design spec's "Why accept-always" section);
/// this result is what the conductor's app uses to decide whether to run its own background
/// broadcast, and what fleet/parent views use to show "who is currently sharing location."
public static class TripBroadcasterRules
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    public static string? Compute(DateTime? driverLastPingAt, DateTime? conductorLastPingAt, DateTime now)
    {
        if (driverLastPingAt is { } d && now - d < StaleAfter) return "driver";
        if (conductorLastPingAt is { } c && now - c < StaleAfter) return "conductor";
        return null;
    }
}
```

- [ ] **Step 4: Run the unit tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter "FullyQualifiedName~TripBroadcasterRulesTests"`
Expected: all 6 tests pass.

- [ ] **Step 5: Add the ping-timestamp columns to `TripResponse` and `TripCols`**

In `src/Sms.Modules.Transport/TransportModule.cs`, change:

```csharp
public sealed record TripResponse(
    Guid Id, Guid TenantId, Guid? RouteId, string? BusNo, Guid? DriverId, Guid? ConductorId,
    string Direction, string Status, DateTime? StartedAt, DateTime? EndedAt);
```

to:

```csharp
public sealed record TripResponse(
    Guid Id, Guid TenantId, Guid? RouteId, string? BusNo, Guid? DriverId, Guid? ConductorId,
    string Direction, string Status, DateTime? StartedAt, DateTime? EndedAt,
    DateTime? DriverLastPingAt = null, DateTime? ConductorLastPingAt = null, string? ActiveBroadcaster = null);
```

(The three trailing properties have defaults, so every existing Dapper-mapped query that doesn't
select those columns — `Trip_Start.sql`, `Trip_End.sql` — keeps working unchanged, defaulting to
`null`. Only `GetCurrentAsync`'s query, changed below, needs them for real.)

Change `TripCols`:

```csharp
    private const string TripCols =
        "Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt";
```

to:

```csharp
    private const string TripCols =
        "Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt, " +
        "DriverLastPingAt, ConductorLastPingAt";
```

Requires a schema change too — go back to Task 1's migration file (`M0154_Buses_ConductorStaffId.cs`)
and add this to its `Up()` method, right after the `ConductorStaffId` column addition:

```csharp
        Execute.Sql(@"
IF COL_LENGTH('dbo.Trips', 'DriverLastPingAt') IS NULL
    ALTER TABLE dbo.Trips ADD DriverLastPingAt datetime2 NULL;
IF COL_LENGTH('dbo.Trips', 'ConductorLastPingAt') IS NULL
    ALTER TABLE dbo.Trips ADD ConductorLastPingAt datetime2 NULL;");
```

And to its `Down()`:

```csharp
        Execute.Sql(@"
IF COL_LENGTH('dbo.Trips', 'DriverLastPingAt') IS NOT NULL
    ALTER TABLE dbo.Trips DROP COLUMN DriverLastPingAt;
IF COL_LENGTH('dbo.Trips', 'ConductorLastPingAt') IS NOT NULL
    ALTER TABLE dbo.Trips DROP COLUMN ConductorLastPingAt;");
```

(Rationale for putting this in Task 1's migration rather than a second one: FluentMigrator
migrations that have already run in an environment can't be edited retroactively — this is only
safe because Task 1 has not been executed/committed in any real environment yet. Editing it now,
inside this same plan's execution, before it's ever applied anywhere, is correct; do NOT edit an
already-applied migration once this plan has been run for real.)

- [ ] **Step 6: Add `MarkPingAsync` and fix `GetCurrentAsync`'s column list usage**

In `src/Sms.Modules.Transport/TransportModule.cs`, add a new method to `TripRepository`, right
after `IngestPingsAsync`:

```csharp
    public Task MarkPingAsync(Guid tripId, string role, CancellationToken ct = default)
    {
        var column = role == "driver" ? "DriverLastPingAt" : "ConductorLastPingAt";
        return ExecuteInlineAsync(
            $"UPDATE dbo.Trips SET {column} = SYSUTCDATETIME() WHERE Id = @tripId", new { tripId }, ct);
    }
```

`GetCurrentAsync` already uses the `TripCols` constant (changed in Step 5), so no further edit is
needed there beyond what Task 2 already did to its `WHERE` clause.

- [ ] **Step 7: Wire arbitration into `TripService`**

In `src/Sms.Application/Services/Transport/TripService.cs`, add `Sms.Shared.Kernel.Time` to the
usings and inject `IClock`:

```csharp
public sealed class TripService(
    TripRepository repo, ITenantContext tenant,
    ITransportFleetBroadcaster fleetBroadcaster, ILiveBroadcaster live, IClock clock) : ITripService
```

Change `StartAsync` to attach the computed field (freshly-started trip has no pings yet, so this
will always compute to `null`, but wiring it here keeps `StartAsync` and `GetCurrentAsync`
consistent for the frontend contract):

```csharp
    public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var trip = (await repo.StartAsync(tid, uid, req, ct))!;
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult<TripResponse>.Ok(WithActiveBroadcaster(trip), 201);
    }
```

Change `GetCurrentAsync`:

```csharp
    public async Task<ApiResult<TripResponse?>> GetCurrentAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<TripResponse?>.Fail(new Error("forbidden", "no user context"), 403);
        var trip = await repo.GetCurrentAsync(uid, ct);
        return ApiResult<TripResponse?>.Ok(trip is null ? null : WithActiveBroadcaster(trip));
    }
```

Change `IngestPingsAsync` to capture the role and mark the ping:

```csharp
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
        return ApiResult.NoContent();
    }
```

Add a private helper at the bottom of the class:

```csharp
    private TripResponse WithActiveBroadcaster(TripResponse trip) =>
        trip with { ActiveBroadcaster = TripBroadcasterRules.Compute(trip.DriverLastPingAt, trip.ConductorLastPingAt, clock.UtcNow) };
```

- [ ] **Step 8: Write the failing integration test**

Add to `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs` a new `[Fact]`, using the
file's existing `App()` helper (which returns `(WebApplicationFactory<Program> App, SpyFleetBroadcaster Fleet, SpyLiveBroadcaster Live)`)
and `StaffClient`/`Data` helpers exactly as the file's existing test already uses them:

```csharp
    [Fact]
    public async Task ActiveBroadcaster_reflects_the_most_recently_pinging_role()
    {
        var (app, _, _) = App();
        await using var _dispose = app;
        var tenantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var conductorUserId = Guid.NewGuid();
        var conductorStaffId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Staff (Id, TenantId, Name, UserId) VALUES (@Id, @TenantId, @Name, @UserId)",
                new { Id = conductorStaffId, TenantId = tenantId, Name = "Priya Rao", UserId = conductorUserId });
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo, ConductorStaffId) VALUES (@Id, @TenantId, @BusNo, @ConductorStaffId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, BusNo = busNo, ConductorStaffId = conductorStaffId });
        }

        var driver = StaffClient(app, tenantId, driverUserId);
        var trip = await Data(await driver.PostAsJsonAsync("/v1/staff/trips",
            new { direction = "pickup", bus_no = busNo }), HttpStatusCode.Created);
        var tripId = trip.GetProperty("id").GetGuid();

        var now = DateTime.UtcNow;
        (await driver.PostAsJsonAsync($"/v1/staff/trips/{tripId}/pings", new
        {
            pings = new[] { new { lat = 1.0, lng = 1.0, speed_kmh = 10, heading = 0, at = now } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var current = await Data(await driver.GetAsync("/v1/staff/trip/current"), HttpStatusCode.OK);
        current.GetProperty("active_broadcaster").GetString().Should().Be("driver");
    }
```

Add `using Dapper;` and `using Microsoft.Data.SqlClient;` to the top of the file if not already
present (check first — `TripBroadcastTests.cs` as written earlier in this project did not need raw
SQL seeding, so these usings are likely new for this test).

- [ ] **Step 9: Run the test to verify it fails, then fix, then verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~TripBroadcastTests"`
Expected first: FAIL (either compile error from copy/paste mismatch — fix the variable names to
match the real file — or a genuine assertion failure if any wiring step above was missed).
After fixing: all tests in the file, including the pre-existing three, PASS.

- [ ] **Step 10: Build the whole solution**

Run: `dotnet build src/Sms.Api`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 11: Commit**

```bash
git add src/Sms.Application/Services/Transport/TripBroadcasterRules.cs tests/Sms.Tests.Unit/Transport/TripBroadcasterRulesTests.cs src/Sms.Modules.Transport/TransportModule.cs src/Sms.Application/Services/Transport/TripService.cs tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs db/Sms.Migrations/M0154_Buses_ConductorStaffId.cs
git commit -m "feat(transport): track per-role ping freshness and compute ActiveBroadcaster (driver preferred)"
```

---

## Task 4: Frontend — conductor auto-broadcasts based on `activeBroadcaster` (sms-staff)

**Files:**
- Modify: `D:\SMS\sms-project\sms-staff\src\data\domain\trip.ts`
- Modify: `D:\SMS\sms-project\sms-staff\src\data\http\mappers.ts`
- Modify: `D:\SMS\sms-project\sms-staff\src\data\mock\trip.repo.ts`
- Create: `D:\SMS\sms-project\sms-staff\src\features\trip\broadcasterArbitration.ts`
- Test: `D:\SMS\sms-project\sms-staff\src\features\trip\__tests__\broadcasterArbitration.test.ts` (new)
- Modify: `D:\SMS\sms-project\sms-staff\src\features\trip\hooks.ts` (`useCurrentTrip` polling)
- Modify: `D:\SMS\sms-project\sms-staff\src\screens\TripScreen.tsx`
- Test: `D:\SMS\sms-project\sms-staff\src\screens\__tests__\TripScreen.conductor.broadcast.test.tsx` (new)

This task is in the **`sms-staff`** repo (`D:\SMS\sms-project\sms-staff`), not `sms-backend`.

**Interfaces:**
- Consumes: backend contract from Task 3 — `TripDTO.active_broadcaster: "driver" | "conductor" | null`.
- Produces: `Trip.activeBroadcaster: 'driver' | 'conductor' | null` (domain type); `decideConductorBroadcastAction(activeBroadcaster, isCurrentlyBroadcasting): 'start' | 'stop' | 'none'` (pure function).

- [ ] **Step 1: Replace `broadcasterId` with `activeBroadcaster` in the domain type**

In `src/data/domain/trip.ts`, change:

```typescript
export interface Trip {
  id: string;
  routeId: string;
  busNo: string;
  driverId: string;
  conductorId?: string;
  direction: TripDirection;
  status: TripStatus;
  startedAt?: string;
  endedAt?: string;
  broadcasterId?: string;
}
```

to:

```typescript
export interface Trip {
  id: string;
  routeId: string;
  busNo: string;
  driverId: string;
  conductorId?: string;
  direction: TripDirection;
  status: TripStatus;
  startedAt?: string;
  endedAt?: string;
  activeBroadcaster?: 'driver' | 'conductor' | null;
}
```

- [ ] **Step 2: Update the HTTP mapper**

In `src/data/http/mappers.ts`, change:

```typescript
export interface TripDTO {
  id: string; route_id: string; bus_no: string; driver_id: string; conductor_id?: string;
  direction: TripDirection; status: TripStatus; started_at?: string; ended_at?: string; broadcaster_id?: string;
}
```

to:

```typescript
export interface TripDTO {
  id: string; route_id: string; bus_no: string; driver_id: string; conductor_id?: string;
  direction: TripDirection; status: TripStatus; started_at?: string; ended_at?: string;
  active_broadcaster?: 'driver' | 'conductor' | null;
}
```

And change:

```typescript
export const toTrip = (d: TripDTO): Trip => ({
  id: d.id, routeId: d.route_id, busNo: d.bus_no, driverId: d.driver_id, conductorId: d.conductor_id,
  direction: d.direction, status: d.status, startedAt: d.started_at, endedAt: d.ended_at, broadcasterId: d.broadcaster_id,
});
```

to:

```typescript
export const toTrip = (d: TripDTO): Trip => ({
  id: d.id, routeId: d.route_id, busNo: d.bus_no, driverId: d.driver_id, conductorId: d.conductor_id,
  direction: d.direction, status: d.status, startedAt: d.started_at, endedAt: d.ended_at,
  activeBroadcaster: d.active_broadcaster,
});
```

- [ ] **Step 3: Update the mock repository**

In `src/data/mock/trip.repo.ts`, in `startTrip`, change:

```typescript
      const trip: Trip = {
        id: store.genId('trip'),
        routeId,
        busNo: store.route.assignedBusNo,
        driverId: store.session.user.id,
        direction,
        status: 'live',
        startedAt: new Date().toISOString(),
        broadcasterId: store.session.user.id,
      };
```

to:

```typescript
      const trip: Trip = {
        id: store.genId('trip'),
        routeId,
        busNo: store.route.assignedBusNo,
        driverId: store.session.user.id,
        direction,
        status: 'live',
        startedAt: new Date().toISOString(),
        activeBroadcaster: 'driver',
      };
```

- [ ] **Step 4: Write the failing unit test for the pure decision function**

Create `src/features/trip/__tests__/broadcasterArbitration.test.ts`:

```typescript
import { decideConductorBroadcastAction } from '@/features/trip/broadcasterArbitration';

describe('decideConductorBroadcastAction', () => {
  it('starts broadcasting when the driver is not active and the conductor is not yet broadcasting', () => {
    expect(decideConductorBroadcastAction(null, false)).toBe('start');
    expect(decideConductorBroadcastAction('conductor', false)).toBe('start');
  });

  it('does nothing when already broadcasting and still needed', () => {
    expect(decideConductorBroadcastAction(null, true)).toBe('none');
    expect(decideConductorBroadcastAction('conductor', true)).toBe('none');
  });

  it('stops broadcasting when the driver becomes active again', () => {
    expect(decideConductorBroadcastAction('driver', true)).toBe('stop');
  });

  it('does nothing when the driver is active and the conductor was never broadcasting', () => {
    expect(decideConductorBroadcastAction('driver', false)).toBe('none');
  });
});
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `npx jest broadcasterArbitration --silent`
Expected: FAIL with `Cannot find module '@/features/trip/broadcasterArbitration'`.

- [ ] **Step 6: Implement the pure function**

Create `src/features/trip/broadcasterArbitration.ts`:

```typescript
export type BroadcastAction = 'start' | 'stop' | 'none';

// The conductor's app should be broadcasting whenever the driver isn't currently the active
// broadcaster (per the backend's ActiveBroadcaster computation) — this decides only what to DO
// about that given the app's own current broadcasting state, so it's idempotent to call on every
// poll tick without double-starting or double-stopping.
export function decideConductorBroadcastAction(
  activeBroadcaster: 'driver' | 'conductor' | null | undefined,
  isCurrentlyBroadcasting: boolean,
): BroadcastAction {
  const shouldBroadcast = activeBroadcaster !== 'driver';
  if (shouldBroadcast && !isCurrentlyBroadcasting) return 'start';
  if (!shouldBroadcast && isCurrentlyBroadcasting) return 'stop';
  return 'none';
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `npx jest broadcasterArbitration --silent`
Expected: 4 tests pass.

- [ ] **Step 8: Add polling to `useCurrentTrip`**

In `src/features/trip/hooks.ts`, change:

```typescript
export function useCurrentTrip() {
  const repos = useRepositories();
  const tenantId = useTenantId();
  return useQuery({
    queryKey: queryKeys.tripCurrent(tenantId),
    queryFn: () => repos.trip.current(),
  });
}
```

to:

```typescript
export function useCurrentTrip() {
  const repos = useRepositories();
  const tenantId = useTenantId();
  return useQuery({
    queryKey: queryKeys.tripCurrent(tenantId),
    queryFn: () => repos.trip.current(),
    refetchInterval: (query) => (query.state.data?.status === 'live' ? 10_000 : false),
  });
}
```

- [ ] **Step 9: Write the failing component test**

Create `src/screens/__tests__/TripScreen.conductor.broadcast.test.tsx`:

```tsx
import React from 'react';
import { render, fireEvent, waitFor, act } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ThemeProvider, useTheme } from '@/theme';
import { RepositoryProvider } from '@/data/repositories/RepositoryContext';
import { createMockRepositories } from '@/data/repositories/factory';
import { createStore } from '@/data/mock/store';
import { TripScreen } from '@/screens/TripScreen';
import { ToastProvider } from '@/components/ui';

jest.mock('@react-native-async-storage/async-storage', () => {
  let mem: Record<string, string> = {};
  return { __esModule: true, default: {
    getItem: jest.fn((k: string) => Promise.resolve(mem[k] ?? null)),
    setItem: jest.fn((k: string, v: string) => { mem[k] = v; return Promise.resolve(); }),
    removeItem: jest.fn((k: string) => { delete mem[k]; return Promise.resolve(); }),
    clear: jest.fn(() => { mem = {}; return Promise.resolve(); }),
  } };
});

const startBroadcast = jest.fn(() => Promise.resolve(true));
const stopBroadcast = jest.fn(() => Promise.resolve());
jest.mock('@/features/trip/broadcaster', () => ({
  startBroadcast: (...args: unknown[]) => startBroadcast(...args),
  stopBroadcast: (...args: unknown[]) => stopBroadcast(...args),
}));

const SetConductor: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { setRole } = useTheme();
  React.useEffect(() => { setRole('conductor'); }, [setRole]);
  return <>{children}</>;
};

async function renderConductor() {
  const store = await createStore();
  const repos = createMockRepositories(store);
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const nav = { goBack: jest.fn(), navigate: jest.fn() };
  const utils = render(
    <SafeAreaProvider initialMetrics={{ frame: { x: 0, y: 0, width: 0, height: 0 }, insets: { top: 0, left: 0, right: 0, bottom: 0 } }}>
      <QueryClientProvider client={qc}>
        <ThemeProvider>
          <ToastProvider>
            <SetConductor>
              <RepositoryProvider repositories={repos}>
                <TripScreen navigation={nav as never} />
              </RepositoryProvider>
            </SetConductor>
          </ToastProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </SafeAreaProvider>,
  );
  return { ...utils, store, qc };
}

it('the conductor auto-starts broadcasting once the driver is no longer active', async () => {
  const { getByTestId, findByText, store, qc } = await renderConductor();
  await findByText(/Route 7/);
  fireEvent.press(getByTestId('trip-start'));
  await waitFor(() => expect(getByTestId('roster-stu_1')).toBeTruthy(), { timeout: 4000 });

  // The mock startTrip() call sets activeBroadcaster: 'driver' (mirrors the real backend's
  // default when a driver starts the trip) — the conductor should NOT auto-broadcast yet.
  expect(startBroadcast).not.toHaveBeenCalled();

  // Simulate the driver going stale: mutate the mock store's current trip directly and force
  // a refetch, the same way a real poll tick would observe the backend's computed field flip.
  await act(async () => {
    if (store.currentTrip) store.currentTrip.activeBroadcaster = null;
    await qc.invalidateQueries();
  });

  await waitFor(() => expect(startBroadcast).toHaveBeenCalledTimes(1));
});
```

- [ ] **Step 10: Run the test to verify it fails**

Run: `npx jest TripScreen.conductor.broadcast --silent`
Expected: FAIL — `startBroadcast` is never called, because `TripScreen.tsx` doesn't yet call
`decideConductorBroadcastAction` anywhere (the `waitFor(() => expect(startBroadcast).toHaveBeenCalledTimes(1))`
assertion times out).

- [ ] **Step 11: Implement the conductor effect in `TripScreen`**

In `src/screens/TripScreen.tsx`, add an import and a ref + effect. Change the imports at the top
from:

```typescript
import { startBroadcast, stopBroadcast } from '@/features/trip/broadcaster';
import { simulateBusPosition } from '@/features/trip/simulateBus';
```

to:

```typescript
import { startBroadcast, stopBroadcast } from '@/features/trip/broadcaster';
import { simulateBusPosition } from '@/features/trip/simulateBus';
import { decideConductorBroadcastAction } from '@/features/trip/broadcasterArbitration';
```

Then, inside the `TripScreen` component, right after the existing `useEffect` that ticks `now`
every 5s (the one starting `// Update 'now' every 5 s while a live trip is active`), add a new
effect and a ref to track the conductor's own broadcasting state:

```typescript
  const conductorBroadcasting = React.useRef(false);

  // Conductor-only: auto start/stop this device's own GPS broadcast based on which role the
  // backend currently treats as the active broadcaster (driver preferred). Runs on every
  // trip/current poll tick while a trip is live — see useCurrentTrip's refetchInterval.
  useEffect(() => {
    if (role.key !== 'conductor' || !trip) return;
    const action = decideConductorBroadcastAction(trip.activeBroadcaster, conductorBroadcasting.current);
    if (action === 'start') {
      conductorBroadcasting.current = true;
      void startBroadcast({ tripId: trip.id, onPing: (p) => repos.trip.publishPing(p) });
    } else if (action === 'stop') {
      conductorBroadcasting.current = false;
      void stopBroadcast();
    }
  }, [role.key, trip, repos.trip]);
```

Place this using the `useEffect` already imported at the top of the file (it already is, per the
existing `import React, { useState, useEffect, useCallback } from 'react';` line — add `useRef` to
that same import line so it reads `import React, { useState, useEffect, useCallback, useRef } from 'react';`
and use `useRef` directly instead of `React.useRef` for consistency with the rest of the file).

Also update `onEnd` so a conductor's leftover broadcast is always stopped when the trip ends,
regardless of arbitration state — change:

```typescript
  const onEnd = async () => {
    if (!trip) return;
    await stopBroadcast().catch(() => {});
    const s = await endTrip.mutateAsync(trip.id);
    setSummary(s);
  };
```


This already unconditionally calls `stopBroadcast()` for both roles, so no change is needed here.

- [ ] **Step 12: Run the test to verify it passes**

Run: `npx jest TripScreen.conductor.broadcast --silent`
Expected: the test from Step 9 passes.

- [ ] **Step 13: Run the full frontend verification**

Run, from the `sms-staff` root:
```bash
npx tsc --noEmit
npx jest --silent
```
Expected: `tsc` reports no errors; the full Jest suite passes, including the pre-existing
`TripScreen.test.tsx`, `TripScreen.conductor.test.tsx`, and this task's new
`broadcasterArbitration.test.ts` and `TripScreen.conductor.broadcast.test.tsx`. If any pre-existing
test now fails, it's a regression from this task's `activeBroadcaster` rename — find and fix it
before moving on (check for any other leftover reference: `grep -rn "broadcasterId\|broadcaster_id" src`).

- [ ] **Step 14: Commit**

```bash
git add src/data/domain/trip.ts src/data/http/mappers.ts src/data/mock/trip.repo.ts src/features/trip/broadcasterArbitration.ts src/features/trip/__tests__/broadcasterArbitration.test.ts src/features/trip/hooks.ts src/screens/TripScreen.tsx src/screens/__tests__/TripScreen.conductor.broadcast.test.tsx
git commit -m "feat(trip): conductor auto-broadcasts based on the backend's computed activeBroadcaster"
```

---

## Final integration check

After all four tasks are committed (Tasks 1-3 in `sms-backend`, Task 4 in `sms-staff`):

- [ ] In `sms-backend`, run `dotnet build src/Sms.Api` and, if a SQL Server instance is reachable
  and no live process holds a build lock, `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~Transport"` —
  all Transport-scoped tests across all four tasks should be green together (not just individually).
- [ ] In `sms-staff`, run `npx tsc --noEmit && npx jest --silent` — full suite green.
- [ ] Manually trace the end-to-end story once more against the spec's goals: a bus with both a
  driver and a conductor assigned, a trip started by the driver, `GET /staff/trip/assignment`
  showing the conductor's name, the conductor legally able to view the roster and board students,
  and — conceptually, since this can't be device-tested here — the conductor's `TripScreen`
  auto-starting its own broadcast once `activeBroadcaster` stops being `"driver"`.
