# Trip Stop/Boarding State Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist which stop a trip is at, detect stop arrival from GPS, require explicit driver confirmation and completion, add a school-arrival checkpoint that keeps the trip open, and standardize boarding states — all riding the existing per-bus SignalR broadcast infrastructure.

**Architecture:** A new `TripStopProgress` table plus a `Trips.CurrentStopId` pointer column replace the need for a large literal state enum. Arrival is detected server-side on every ping (advisory), confirmed and completed via two new driver-initiated endpoints (authoritative). Return trips are new `Trip` rows with `Direction='drop'`. Three new broadcast events extend the already-authorized `bus:{busId}` group.

**Tech Stack:** ASP.NET Core, Dapper (raw SQL, no ORM), SQL Server with row-level security, FluentMigrator, xUnit + FluentAssertions (no mocking framework — DB-touching code is tested via `SqlServerFixture` real-database integration tests; only pure in-memory logic gets plain unit tests).

**Spec:** `docs/superpowers/specs/2026-09-04-trip-stop-boarding-state-machine-design.md`

## Global Constraints

- `Trips.Status` is `nvarchar(10)` — the new `'arrived'` value (7 chars) fits without widening the column. Never introduce a status value longer than 10 characters.
- Stop confirmation/completion must happen in `Seq` order — no skipping ahead. Confirming a stop that isn't the trip's next incomplete stop is a `409`.
- Arrival *detection* (via ping ingest) is advisory only and never mutates `Trips`/`TripStopProgress` by itself — only the two new explicit endpoints (`confirm-arrival`, `complete`) change state.
- Return trips are new `Trip` rows (`Direction='drop'`) — never a mutation of an existing trip's `Direction`.
- `school-arrived` sets `Status='arrived'` but never ends the trip — `EndAsync` is unchanged and remains the only way to close a trip.
- No mocking framework in this codebase. DB-touching code is tested via `SqlServerFixture` (real SQL Server, migrations auto-run via `MigrationRunner.Run`, `[Collection("sql")]`). Pure logic (e.g. the arrival-radius distance check) gets a plain xUnit unit test.
- Migration files follow `M{4-digit}_{PascalCaseDescription}.cs`, `[Migration({int}, "description")]` matching the numeric prefix. Next available number is **M0176** — confirm this is still unclaimed (`ls db/Sms.Migrations/M01*.cs | sort | tail -5`) immediately before creating the file, since other work may have landed a migration in the meantime.
- New procs load via `M0003_Procs_Auth.EmbeddedProcs("procs.tripstops.")` (a fresh sub-namespace, not an existing broad prefix, to avoid an earlier migration's `EmbeddedProcs` call picking these up before the table exists on a fresh-DB replay — the established convention per `M0171`'s comment).
- Integration tests need `Jwt__SigningKey` set as an environment variable to actually execute (not just build) — e.g. `export Jwt__SigningKey="compose-dev-signing-key-at-least-32-bytes!!"` before `dotnet test`, since these tests force `environment=Production` and don't load `appsettings.Development.json`'s key.

---

### Task 1: Schema migration — TripStopProgress, CurrentStopId, ping Accuracy

**Files:**
- Create: `db/Sms.Migrations/M0176_TripStopProgress_And_PingAccuracy.cs`
- Create: `db/Sms.Migrations/procs/tripstops/TripStopProgress_ConfirmArrival.sql`
- Create: `db/Sms.Migrations/procs/tripstops/TripStopProgress_Complete.sql`
- Modify: `db/Sms.Migrations/procs/transport/TripPing_BulkInsert.sql`
- Test: `tests/Sms.Tests.Integration/Transport/TripStopProgressSchemaTests.cs` (new)

**Interfaces:**
- Consumes: nothing (pure schema).
- Produces: `dbo.TripStopProgress` table, `dbo.Trips.CurrentStopId` column, `dbo.TripPings.Accuracy` column, `dbo.TripPingTvp` recreated with an `Accuracy` column, `dbo.TripStopProgress_ConfirmArrival`/`dbo.TripStopProgress_Complete` procs — consumed by Task 3 (repository methods) and Task 2 (ping ingestion).

- [ ] **Step 1: Confirm the migration number is still free**

Run: `ls db/Sms.Migrations/M01*.cs | sort | tail -5`
Expected: highest existing file is `M0175_...` — if a higher one now exists, use the next number instead and adjust every reference below accordingly.

- [ ] **Step 2: Write the migration**

```csharp
// db/Sms.Migrations/M0176_TripStopProgress_And_PingAccuracy.cs
using FluentMigrator;

namespace Sms.Migrations;

[Migration(176, "TripStopProgress table, Trips.CurrentStopId, TripPings.Accuracy")]
public sealed class M0176_TripStopProgress_And_PingAccuracy : Migration
{
    public override void Up()
    {
        Create.Table("TripStopProgress")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TripId").AsGuid().NotNullable()
            .WithColumn("StopId").AsGuid().NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("ArrivedAt").AsDateTime2().Nullable()
            .WithColumn("ConfirmedAt").AsDateTime2().Nullable()
            .WithColumn("DepartedAt").AsDateTime2().Nullable();
        Create.Index("IX_TripStopProgress_Trip_Seq").OnTable("TripStopProgress")
            .OnColumn("TripId").Ascending().OnColumn("Seq").Ascending();
        Create.Index("IX_TripStopProgress_Trip_Stop").OnTable("TripStopProgress")
            .OnColumn("TripId").Ascending().OnColumn("StopId").Ascending().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.TripStopProgressTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TripStopProgress,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TripStopProgress AFTER INSERT
WITH (STATE = ON);");

        Alter.Table("Trips").AddColumn("CurrentStopId").AsGuid().Nullable();

        Alter.Table("TripPings").AddColumn("Accuracy").AsDouble().Nullable();

        // SQL Server has no ALTER TYPE for table types — drop and recreate, and the
        // consuming proc must be dropped first since it references the type signature.
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripPing_BulkInsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.TripPingTvp;");
        Execute.Sql(@"CREATE TYPE dbo.TripPingTvp AS TABLE
(
    Lat float NOT NULL,
    Lng float NOT NULL,
    SpeedKmh float NOT NULL,
    Heading float NOT NULL,
    At datetime2 NOT NULL,
    Accuracy float NULL
);");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.tripstops."))
            Execute.Sql(sql);
        // TripPing_BulkInsert.sql lives under procs.transport. and is re-loaded here
        // (its own migration's EmbeddedProcs call already ran once; this CREATE OR ALTER
        // re-applies the updated version now that the TVP shape changed).
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripStopProgress_Complete;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripStopProgress_ConfirmArrival;");

        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripPing_BulkInsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.TripPingTvp;");
        Execute.Sql(@"CREATE TYPE dbo.TripPingTvp AS TABLE
(
    Lat float NOT NULL,
    Lng float NOT NULL,
    SpeedKmh float NOT NULL,
    Heading float NOT NULL,
    At datetime2 NOT NULL
);");
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);

        Delete.Column("Accuracy").FromTable("TripPings");
        Delete.Column("CurrentStopId").FromTable("Trips");

        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TripStopProgressTenantPolicy;");
        Delete.Table("TripStopProgress");
    }
}
```

**Note:** re-running `procs.transport.` `EmbeddedProcs` in this migration reloads every proc under that namespace fragment, not just `TripPing_BulkInsert.sql` — confirm this is harmless (a `CREATE OR ALTER` re-application of already-correct procs) by checking that every `.sql` file under `db/Sms.Migrations/procs/transport/` uses `CREATE OR ALTER PROCEDURE` (matching `Boarding_Upsert.sql`'s pattern) before relying on this; if any file in that folder uses plain `CREATE PROCEDURE` (which would fail on re-run), only re-load `TripPing_BulkInsert.sql` specifically instead — check `M0003_Procs_Auth.EmbeddedProcs`'s signature for whether it accepts a specific filename or only a folder prefix, and adjust to a narrower call if needed.

- [ ] **Step 3: Write the two new stored procs**

```sql
-- db/Sms.Migrations/procs/tripstops/TripStopProgress_ConfirmArrival.sql
CREATE OR ALTER PROCEDURE dbo.TripStopProgress_ConfirmArrival
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @StopId uniqueidentifier,
    @Seq int, @ArrivedAt datetime2, @ConfirmedAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.TripStopProgress AS tgt
    USING (SELECT @TripId AS TripId, @StopId AS StopId) AS src
        ON tgt.TenantId = @TenantId AND tgt.TripId = src.TripId AND tgt.StopId = src.StopId
    WHEN MATCHED THEN
        UPDATE SET ArrivedAt = ISNULL(tgt.ArrivedAt, @ArrivedAt), ConfirmedAt = @ConfirmedAt
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, TripId, StopId, Seq, ArrivedAt, ConfirmedAt)
        VALUES (NEWID(), @TenantId, @TripId, @StopId, @Seq, @ArrivedAt, @ConfirmedAt);

    UPDATE dbo.Trips SET CurrentStopId = @StopId WHERE Id = @TripId AND TenantId = @TenantId;
END
```

```sql
-- db/Sms.Migrations/procs/tripstops/TripStopProgress_Complete.sql
CREATE OR ALTER PROCEDURE dbo.TripStopProgress_Complete
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @StopId uniqueidentifier, @DepartedAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TripStopProgress SET DepartedAt = @DepartedAt
    WHERE TenantId = @TenantId AND TripId = @TripId AND StopId = @StopId;

    UPDATE dbo.Trips SET CurrentStopId = NULL WHERE Id = @TripId AND TenantId = @TenantId;
END
```

- [ ] **Step 4: Update `TripPing_BulkInsert.sql` for the new column**

```sql
-- db/Sms.Migrations/procs/transport/TripPing_BulkInsert.sql
CREATE OR ALTER PROCEDURE dbo.TripPing_BulkInsert
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @Rows dbo.TripPingTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At, Accuracy)
    SELECT NEWID(), @TenantId, @TripId, Lat, Lng, SpeedKmh, Heading, At, Accuracy FROM @Rows;
END
```

- [ ] **Step 5: Write the failing schema test**

```csharp
// tests/Sms.Tests.Integration/Transport/TripStopProgressSchemaTests.cs
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Dapper;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TripStopProgressSchemaTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    [Fact]
    public async Task Migration_creates_TripStopProgress_and_new_columns()
    {
        await using var app = App(); // forces migrations to have run via SqlServerFixture.InitializeAsync
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();

        var tripStopProgressExists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = 'TripStopProgress'");
        tripStopProgressExists.Should().Be(1);

        var currentStopIdExists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Trips') AND name = 'CurrentStopId'");
        currentStopIdExists.Should().Be(1);

        var accuracyExists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.TripPings') AND name = 'Accuracy'");
        accuracyExists.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmArrival_and_Complete_procs_round_trip()
    {
        var tenantId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var stopId = Guid.NewGuid();

        await using var app = App();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Trips (Id, TenantId, BusId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId });

        await conn.ExecuteAsync("dbo.TripStopProgress_ConfirmArrival",
            new { TenantId = tenantId, TripId = tripId, StopId = stopId, Seq = 1, ArrivedAt = DateTime.UtcNow, ConfirmedAt = DateTime.UtcNow },
            commandType: System.Data.CommandType.StoredProcedure);

        var currentStopId = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT CurrentStopId FROM dbo.Trips WHERE Id = @tripId", new { tripId });
        currentStopId.Should().Be(stopId);

        await conn.ExecuteAsync("dbo.TripStopProgress_Complete",
            new { TenantId = tenantId, TripId = tripId, StopId = stopId, DepartedAt = DateTime.UtcNow },
            commandType: System.Data.CommandType.StoredProcedure);

        var afterComplete = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT CurrentStopId FROM dbo.Trips WHERE Id = @tripId", new { tripId });
        afterComplete.Should().BeNull();

        var departedAt = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT DepartedAt FROM dbo.TripStopProgress WHERE TripId = @tripId AND StopId = @stopId", new { tripId, stopId });
        departedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopProgressSchemaTests`
Expected: PASS (2 tests). If no live SQL Server is reachable, run `dotnet build` instead and note the limitation.

Run: `dotnet test tests/Sms.Tests.Integration` (broad — confirms the recreated `TripPingTvp`/`TripPing_BulkInsert` didn't break existing ping-ingestion tests)
Expected: no new failures versus the pre-task baseline.

- [ ] **Step 7: Commit**

```bash
git add db/Sms.Migrations/M0176_TripStopProgress_And_PingAccuracy.cs db/Sms.Migrations/procs/tripstops/ db/Sms.Migrations/procs/transport/TripPing_BulkInsert.sql tests/Sms.Tests.Integration/Transport/TripStopProgressSchemaTests.cs
git commit -m "feat(transport): add TripStopProgress table, Trips.CurrentStopId, TripPings.Accuracy"
```

---

### Task 2: Ping accuracy threading

**Files:**
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` — `PingItem`, `TripRepository.IngestPingsAsync`.
- Modify: `src/Sms.Modules.Transport/BusModule.cs` — `BusLiveSnapshotResponse`, `LiveSnapshotPingRow`, `GetLiveSnapshotAsync`.
- Test: extend `tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs`.

**Interfaces:**
- Consumes: Task 1's `dbo.TripPings.Accuracy`/`dbo.TripPingTvp` columns.
- Produces: `PingItem.Accuracy` (nullable double, default `null` so existing callers compile unchanged), `BusLiveSnapshotResponse.Accuracy` — consumed by Task 4's broadcast payload.

- [ ] **Step 1: Write the failing test**

Add to `tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs` (extend the existing `SeedBusWithLastPing` helper to accept an optional accuracy, or add a new seed variant):

```csharp
[Fact]
public async Task Snapshot_carries_the_pings_accuracy()
{
    var tenantId = Guid.NewGuid();
    var busId = Guid.NewGuid();
    var tripId = Guid.NewGuid();
    await using (var conn = new SqlConnection(fx.ConnectionString))
    {
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Trips (Id, TenantId, BusId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At, Accuracy)
              VALUES (@Id, @TenantId, @TripId, 12.1, 77.1, 5, 90, SYSUTCDATETIME(), 8.0)",
            new { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId });
    }
    await using var app = App();
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
    var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

    (await repo.GetLiveSnapshotAsync(busId, default)).Accuracy.Should().Be(8.0);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter BusLiveSnapshotTests`
Expected: FAIL — `BusLiveSnapshotResponse` has no `Accuracy` member yet.

- [ ] **Step 3: Add `Accuracy` to `PingItem` and thread it through ingestion**

In `src/Sms.Modules.Transport/TransportModule.cs`:

```csharp
public sealed record PingItem(double Lat, double Lng, double SpeedKmh, double Heading, DateTime At, double? Accuracy = null);
```

In `TripRepository.IngestPingsAsync`, add the column to the `DataTable` and row population:

```csharp
table.Columns.Add("Accuracy", typeof(double));
// ...
foreach (var p in pings) table.Rows.Add(p.Lat, p.Lng, p.SpeedKmh, p.Heading, p.At, (object?)p.Accuracy ?? DBNull.Value);
```

**Note:** confirm the exact current body of `IngestPingsAsync` before editing (it was read in full during planning, but re-read it directly since another task may have touched it) — match its existing column-add ordering exactly so the `DataTable` columns line up with `dbo.TripPingTvp`'s `(Lat, Lng, SpeedKmh, Heading, At, Accuracy)` order from Task 1.

- [ ] **Step 4: Add `Accuracy` to `BusLiveSnapshotResponse` and `GetLiveSnapshotAsync`**

In `src/Sms.Modules.Transport/BusModule.cs`:

```csharp
public sealed record BusLiveSnapshotResponse(
    Guid BusId, Guid? TripId,
    double? Lat, double? Lng, double SpeedKmh, double Heading,
    string Status, DateTime? LastUpdateAt,
    int? EtaNextStopMin, string? NextStopName, double? Accuracy)
{
    public Guid? CurrentStopId { get; init; }
    public bool WithinArrivalRadius { get; init; }
    public Guid? NextStopId { get; init; }
}
```

(`CurrentStopId`/`WithinArrivalRadius`/`NextStopId` are `init`-only, non-constructor properties — same pattern `TripResponse.ActiveBroadcaster` already uses — because Task 4 sets them via `with` from `TripService`, outside this bus-centric repository method, which has no knowledge of trip-stop-progress.)

Update `LiveSnapshotPingRow` and the query to select `Accuracy`:

```csharp
private sealed record LiveSnapshotPingRow(double Lat, double Lng, double SpeedKmh, double Heading, DateTime At, double? Accuracy);
```

```csharp
var ping = tripId is null ? null : (await QueryInlineAsync<LiveSnapshotPingRow>(
    "SELECT TOP 1 Lat, Lng, SpeedKmh, Heading, At, Accuracy FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At DESC",
    new { tripId }, ct)).FirstOrDefault();
```

Update the return statement to pass `ping?.Accuracy` as the new constructor argument (last positional parameter, per the record shape above).

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter BusLiveSnapshotTests`
Expected: PASS (7 tests: the existing 6 plus this new one).

Run: `dotnet build` (whole solution)
Expected: 0 errors — confirms every other constructor call site of `BusLiveSnapshotResponse` (there should be exactly one, inside `GetLiveSnapshotAsync` itself) still compiles with the new positional parameter.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Modules.Transport/TransportModule.cs src/Sms.Modules.Transport/BusModule.cs tests/Sms.Tests.Integration/Transport/BusLiveSnapshotTests.cs
git commit -m "feat(transport): thread GPS accuracy from ping ingestion to the live snapshot"
```

---

### Task 3: Repository methods — next incomplete stop, arrival radius check, stop progress CRUD

**Files:**
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` — add to `TripRepository`.
- Create: `src/Sms.Application/Services/Transport/StopArrivalRules.cs` (pure logic)
- Test: `tests/Sms.Tests.Unit/Transport/StopArrivalRulesTests.cs` (new)
- Test: `tests/Sms.Tests.Integration/Transport/TripStopRepositoryTests.cs` (new)

**Interfaces:**
- Consumes: Task 1's `TripStopProgress_ConfirmArrival`/`TripStopProgress_Complete` procs and `TripStopProgress` table.
- Produces:
  - `StopArrivalRules.IsWithinRadius(double distanceMeters, double radiusMeters) -> bool` — pure, consumed by Task 4.
  - `TripRepository.GetNextIncompleteStopAsync(Guid tripId, Guid routeId, CancellationToken ct = default) -> Task<NextStopRow?>` where `NextStopRow(Guid Id, string Name, double Lat, double Lng, int Seq)` — consumed by Task 4.
  - `TripRepository.ConfirmStopArrivalAsync(Guid tenantId, Guid tripId, Guid stopId, int seq, DateTime arrivedAt, DateTime confirmedAt, CancellationToken ct = default) -> Task` — consumed by Task 5.
  - `TripRepository.CompleteStopAsync(Guid tenantId, Guid tripId, Guid stopId, DateTime departedAt, CancellationToken ct = default) -> Task` — consumed by Task 5.
  - `TripRepository.GetCurrentStopIdAsync(Guid tripId, CancellationToken ct = default) -> Task<Guid?>` — consumed by Task 5's ordering/state validation.

- [ ] **Step 1: Write the failing pure-logic unit test**

```csharp
// tests/Sms.Tests.Unit/Transport/StopArrivalRulesTests.cs
using Sms.Application.Services.Transport;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Unit.Transport;

public class StopArrivalRulesTests
{
    [Fact]
    public void Within_radius_is_true_when_distance_is_less_than_radius()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 40, radiusMeters: 100).Should().BeTrue();
    }

    [Fact]
    public void Within_radius_is_false_when_distance_exceeds_radius()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 150, radiusMeters: 100).Should().BeFalse();
    }

    [Fact]
    public void Within_radius_is_true_at_exactly_the_boundary()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 100, radiusMeters: 100).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter StopArrivalRulesTests`
Expected: FAIL — `StopArrivalRules` doesn't exist.

- [ ] **Step 3: Write `StopArrivalRules`**

```csharp
// src/Sms.Application/Services/Transport/StopArrivalRules.cs
namespace Sms.Application.Services.Transport;

/// Pure distance-vs-radius check for stop-arrival detection — the actual
/// Haversine distance computation lives in TripRepository (matching this
/// codebase's existing convention of a private per-class Haversine helper,
/// e.g. BusRepository.GetPositionAsync and AttendanceModule.PunchAsync each
/// have their own), this is just the boundary comparison, kept separate so
/// it's testable without a database.
public static class StopArrivalRules
{
    public static bool IsWithinRadius(double distanceMeters, double radiusMeters) =>
        distanceMeters <= radiusMeters;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter StopArrivalRulesTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the failing integration test for the repository methods**

```csharp
// tests/Sms.Tests.Integration/Transport/TripStopRepositoryTests.cs
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
public class TripStopRepositoryTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    private async Task<(Guid tenantId, Guid routeId, Guid tripId, Guid stop1, Guid stop2)> Seed()
    {
        var tenantId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stop1 = Guid.NewGuid();
        var stop2 = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Trips (Id, TenantId, BusId, RouteId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, @RouteId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId, RouteId = routeId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.RouteStops (Id, TenantId, RouteId, Name, Seq, Lat, Lng) VALUES
              (@S1, @TenantId, @RouteId, 'Stop A', 1, 12.1, 77.1),
              (@S2, @TenantId, @RouteId, 'Stop B', 2, 12.2, 77.2)",
            new { S1 = stop1, S2 = stop2, TenantId = tenantId, RouteId = routeId });
        return (tenantId, routeId, tripId, stop1, stop2);
    }

    [Fact]
    public async Task GetNextIncompleteStopAsync_returns_the_first_stop_when_none_completed()
    {
        var (tenantId, routeId, tripId, stop1, _) = await Seed();
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, default);
        next.Should().NotBeNull();
        next!.Id.Should().Be(stop1);
    }

    [Fact]
    public async Task ConfirmArrival_sets_CurrentStopId_and_Complete_advances_to_the_next_stop()
    {
        var (tenantId, routeId, tripId, stop1, stop2) = await Seed();
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        await repo.ConfirmStopArrivalAsync(tenantId, tripId, stop1, 1, DateTime.UtcNow, DateTime.UtcNow, default);
        (await repo.GetCurrentStopIdAsync(tripId, default)).Should().Be(stop1);

        await repo.CompleteStopAsync(tenantId, tripId, stop1, DateTime.UtcNow, default);
        (await repo.GetCurrentStopIdAsync(tripId, default)).Should().BeNull();

        var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, default);
        next!.Id.Should().Be(stop2);
    }

    [Fact]
    public async Task GetNextIncompleteStopAsync_returns_null_when_all_stops_completed()
    {
        var (tenantId, routeId, tripId, stop1, stop2) = await Seed();
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();

        await repo.ConfirmStopArrivalAsync(tenantId, tripId, stop1, 1, DateTime.UtcNow, DateTime.UtcNow, default);
        await repo.CompleteStopAsync(tenantId, tripId, stop1, DateTime.UtcNow, default);
        await repo.ConfirmStopArrivalAsync(tenantId, tripId, stop2, 2, DateTime.UtcNow, DateTime.UtcNow, default);
        await repo.CompleteStopAsync(tenantId, tripId, stop2, DateTime.UtcNow, default);

        (await repo.GetNextIncompleteStopAsync(tripId, routeId, default)).Should().BeNull();
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopRepositoryTests`
Expected: FAIL — the new `TripRepository` methods don't exist yet.

- [ ] **Step 7: Add the repository methods**

In `src/Sms.Modules.Transport/TransportModule.cs`, add to `TripRepository`:

```csharp
public sealed record NextStopRow(Guid Id, string Name, double Lat, double Lng, int Seq);

/// The trip's next stop (by Seq) that has no TripStopProgress row with a
/// DepartedAt set — i.e. not yet completed. Null once every stop on the
/// route has been completed. Stops must be confirmed/completed in this
/// order; the confirm-arrival endpoint (Task 5) rejects out-of-order calls.
public async Task<NextStopRow?> GetNextIncompleteStopAsync(Guid tripId, Guid routeId, CancellationToken ct = default) =>
    (await QueryInlineAsync<NextStopRow>(
        @"SELECT TOP 1 rs.Id, rs.Name, rs.Lat, rs.Lng, rs.Seq
          FROM dbo.RouteStops rs
          WHERE rs.RouteId = @routeId
            AND NOT EXISTS (
                SELECT 1 FROM dbo.TripStopProgress tsp
                WHERE tsp.TripId = @tripId AND tsp.StopId = rs.Id AND tsp.DepartedAt IS NOT NULL)
          ORDER BY rs.Seq",
        new { routeId, tripId }, ct)).FirstOrDefault();

public Task ConfirmStopArrivalAsync(Guid tenantId, Guid tripId, Guid stopId, int seq, DateTime arrivedAt, DateTime confirmedAt, CancellationToken ct = default) =>
    ExecuteProcAsync("dbo.TripStopProgress_ConfirmArrival",
        new { TenantId = tenantId, TripId = tripId, StopId = stopId, Seq = seq, ArrivedAt = arrivedAt, ConfirmedAt = confirmedAt }, ct);

public Task CompleteStopAsync(Guid tenantId, Guid tripId, Guid stopId, DateTime departedAt, CancellationToken ct = default) =>
    ExecuteProcAsync("dbo.TripStopProgress_Complete",
        new { TenantId = tenantId, TripId = tripId, StopId = stopId, DepartedAt = departedAt }, ct);

public async Task<Guid?> GetCurrentStopIdAsync(Guid tripId, CancellationToken ct = default) =>
    (await QueryInlineAsync<Guid?>("SELECT CurrentStopId FROM dbo.Trips WHERE Id = @tripId", new { tripId }, ct)).FirstOrDefault();
```

**Note:** confirm `BaseRepository.ExecuteProcAsync`'s exact signature (`Task<int> ExecuteProcAsync(string proc, object? args = null, CancellationToken ct = default)` per prior investigation) still matches before finalizing — these two calls discard the returned row count, which is the same pattern `UpsertBoardingAsync` already uses (`Task` return, not `Task<int>`), so wrap in a lambda or accept the `Task<int>` covariance if the compiler requires an explicit discard.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopRepositoryTests`
Expected: PASS (3 tests). If no live SQL Server is reachable, run `dotnet build` instead and note the limitation.

- [ ] **Step 9: Commit**

```bash
git add src/Sms.Application/Services/Transport/StopArrivalRules.cs tests/Sms.Tests.Unit/Transport/StopArrivalRulesTests.cs src/Sms.Modules.Transport/TransportModule.cs tests/Sms.Tests.Integration/Transport/TripStopRepositoryTests.cs
git commit -m "feat(transport): add next-incomplete-stop query and stop-progress repository methods"
```

---

### Task 4: Wire ping ingestion to compute arrival radius + `StartAsync` duplicate-trip guard

**Files:**
- Modify: `src/Sms.Application/Services/Transport/TripService.cs`
- Modify: `src/Sms.Modules.Transport/TransportModule.cs` — add `TripRepository.HasActiveTripOnBusAsync`.
- Test: extend `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs` and/or a new focused test file.

**Interfaces:**
- Consumes: `TripRepository.GetNextIncompleteStopAsync`/`GetCurrentStopIdAsync` (Task 3), `StopArrivalRules.IsWithinRadius` (Task 3), `BusLiveSnapshotResponse`'s `with`-settable properties (Task 2).
- Produces: `TripRepository.HasActiveTripOnBusAsync(Guid busId, CancellationToken ct = default) -> Task<bool>` — used only inside this task's `StartAsync` change, not consumed elsewhere.

- [ ] **Step 1: Read `TripRepository.StartAsync`'s current body directly**

Run: `grep -n "public.*StartAsync" -A 25 src/Sms.Modules.Transport/TransportModule.cs`

Confirm exactly how `busId` is resolved from `StartTripRequest.BusNo`/`RouteId` (by `BusNo` lookup, by `RouteId`'s associated bus, or both) — this determines whether the duplicate-trip guard in Step 3 below can check by `BusNo` string match or needs the resolved `BusId` first. Adjust Step 3's query/parameter to match whatever the real resolution logic uses; do not guess.

- [ ] **Step 2: Write the failing test for the duplicate-trip guard**

Add to `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs` (or a new file, following its existing JWT/seed helper style):

```csharp
[Fact]
public async Task Starting_a_second_trip_on_a_bus_already_live_is_rejected()
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
    var token = IssueToken(driverId, tenantId, Policies.Driver);
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", token);

    var first = await client.PostAsJsonAsync("/v1/staff/trips", new { direction = "pickup", bus_no = "BUS-1" });
    first.IsSuccessStatusCode.Should().BeTrue();

    var second = await client.PostAsJsonAsync("/v1/staff/trips", new { direction = "pickup", bus_no = "BUS-1" });
    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
}
```

Adapt this test's exact HTTP shape (`IssueToken`, `App()`) to match whatever helpers already exist in the file it's added to — do not duplicate a second incompatible helper set in the same file.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter Starting_a_second_trip`
Expected: FAIL — currently both calls succeed (or fail for an unrelated reason if `BUS-1` isn't otherwise valid — confirm the failure reason is "no guard exists yet", not a seed problem).

- [ ] **Step 4: Add the guard**

In `src/Sms.Modules.Transport/TransportModule.cs`, add to `TripRepository` (adjust the `WHERE` clause per Step 1's findings on how `busId`/`BusNo` resolution actually works):

```csharp
public async Task<bool> HasActiveTripOnBusAsync(Guid busId, CancellationToken ct = default) =>
    (await QueryInlineAsync<int>(
        "SELECT COUNT(1) FROM dbo.Trips WHERE BusId = @busId AND Status IN ('live', 'arrived')",
        new { busId }, ct)).FirstOrDefault() > 0;
```

In `src/Sms.Application/Services/Transport/TripService.cs`'s `StartAsync`, add the check before calling `repo.StartAsync` (exact placement depends on where `busId` becomes known per Step 1 — if `StartAsync` resolves it internally rather than the caller, this check may need to move inside `TripRepository.StartAsync` itself, immediately before the insert, returning a sentinel the service layer translates to `409`; note whichever shape is correct in the commit message):

```csharp
public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
{
    if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
        return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
    // New: resolve the target bus and reject if it already has a live/arrived trip.
    // Exact resolution call depends on Step 1's findings — placeholder shown assumes
    // a resolvable busId is available before repo.StartAsync's insert.
    if (await repo.ResolveBusIdAsync(req, tid, ct) is { } targetBusId
        && await repo.HasActiveTripOnBusAsync(targetBusId, ct))
        return ApiResult<TripResponse>.Fail(new Error("bus_already_active", "This bus already has an active trip"), 409);

    var trip = (await repo.StartAsync(tid, uid, req, ct))!;
    // ... rest unchanged
}
```

**This step's exact code depends entirely on Step 1's findings** — if `StartAsync`'s existing body already resolves `busId` via a helper method, reuse that helper's name instead of inventing `ResolveBusIdAsync`; if resolution only happens as part of the `INSERT` statement itself (e.g. a subquery against `BusNo`), add the guard as a `WHERE NOT EXISTS (...)` clause inside the stored proc `StartAsync` calls, or as a separate `SELECT` immediately before it in the same repository method, and have it return `null` instead of inserting when blocked, with `TripService.StartAsync` translating a `null` result to the `409`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter Starting_a_second_trip`
Expected: PASS.

Run: `dotnet test tests/Sms.Tests.Integration --filter Trip` (broad)
Expected: no regressions to existing trip start/ping/end tests.

- [ ] **Step 6: Wire arrival-radius computation into `IngestPingsAsync`**

In `TripService.IngestPingsAsync`, after the existing `snapshot`/`BroadcastPositionAsync` block, compute and merge the arrival fields (requires the trip's `RouteId` — read it via the existing `repo.GetBusIdAsync`-adjacent pattern or a new small lookup if `RouteId` isn't already available in this method's scope; check `TripRepository`'s existing methods for one that returns `RouteId` for a `tripId`, or add a one-line `SELECT RouteId FROM dbo.Trips WHERE Id = @tripId` inline query if none exists):

```csharp
if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
{
    var snapshot = await buses.GetLiveSnapshotAsync(busId, ct);
    var currentStopId = await repo.GetCurrentStopIdAsync(tripId, ct);
    // Only probe for a next stop when not already sitting at a confirmed one —
    // arrival detection targets the NEXT stop, not the current one.
    if (currentStopId is null && await repo.GetTripRouteIdAsync(tripId, ct) is { } routeId
        && await repo.GetNextIncompleteStopAsync(tripId, routeId, ct) is { } nextStop
        && snapshot.Lat is { } lat && snapshot.Lng is { } lng)
    {
        var distance = Haversine(lat, lng, nextStop.Lat, nextStop.Lng);
        var withinRadius = StopArrivalRules.IsWithinRadius(distance, ArrivalRadiusMeters);
        snapshot = snapshot with { NextStopId = nextStop.Id, WithinArrivalRadius = withinRadius, CurrentStopId = currentStopId };
    }
    else
    {
        snapshot = snapshot with { CurrentStopId = currentStopId };
    }
    await fleetBroadcaster.BroadcastPositionAsync(busId, snapshot, ct);
}
```

Add a private static `Haversine` method to `TripService` (or `TripRepository`, whichever this codebase's convention favors for a trip-lifecycle-adjacent calculation — check whether `TripRepository` already has one before duplicating; if not, add it matching `AttendanceModule`'s exact formula) and a config-driven `ArrivalRadiusMeters` (read via injected `IConfiguration`, key `TransportStops:ArrivalRadiusMeters`, default `100`, following the same `Math.Clamp`-on-read style `TransportOfflineSweepWorker` uses for its own config value).

Add `TripRepository.GetTripRouteIdAsync(Guid tripId, CancellationToken ct = default) -> Task<Guid?>` (simple inline `SELECT RouteId FROM dbo.Trips WHERE Id = @tripId`) if no existing method already returns a trip's `RouteId`.

- [ ] **Step 7: Write a test proving the radius signal reaches a subscriber**

Extend `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs`'s existing `HubConnection`-listening pattern (from the prior plan's Task 6) with an assertion that `position_update`'s payload includes `within_arrival_radius: true` when a ping is sent from coordinates matching a seeded stop's exact `Lat`/`Lng`.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter Trip`
Expected: PASS, including the new radius-signal test.

- [ ] **Step 9: Commit**

```bash
git add src/Sms.Application/Services/Transport/TripService.cs src/Sms.Modules.Transport/TransportModule.cs tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs
git commit -m "feat(transport): compute stop-arrival radius on ping ingest, reject starting a second active trip on a bus"
```

---

### Task 5: New endpoints — confirm-arrival, complete, school-arrived, boarding state validation

**Files:**
- Modify: `src/Sms.Api/Controllers/TripController.cs`
- Modify: `src/Sms.Application/Services/Transport/ITripService.cs` / `TripService.cs`

**Interfaces:**
- Consumes: `TripRepository.ConfirmStopArrivalAsync`/`CompleteStopAsync`/`GetCurrentStopIdAsync`/`GetNextIncompleteStopAsync` (Task 3), `StopArrivalRules.IsWithinRadius` (Task 3).
- Produces: `ITripService.ConfirmStopArrivalAsync`/`CompleteStopAsync`/`MarkSchoolArrivedAsync` — consumed by Task 6's broadcaster wiring inside these same methods.

- [ ] **Step 1: Write the failing tests**

New file `tests/Sms.Tests.Integration/Transport/TripStopEndpointsTests.cs`, following the `TripOwnershipTests.cs` template (JWT issuance, raw-SQL seeding, real HTTP calls, explicit status-code assertions):

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Dapper;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TripStopEndpointsTests(SqlServerFixture fx)
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

    private async Task<(Guid tenantId, Guid tripId, Guid driverId, Guid stop1, Guid stop2)> SeedLiveTripWithTwoStops()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stop1 = Guid.NewGuid();
        var stop2 = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo, DriverStaffId) VALUES (@Id, @TenantId, 'BUS-1', @DriverId)",
            new { Id = busId, TenantId = tenantId, DriverId = driverId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.RouteStops (Id, TenantId, RouteId, Name, Seq, Lat, Lng) VALUES
              (@S1, @TenantId, @RouteId, 'Stop A', 1, 12.1000, 77.1000),
              (@S2, @TenantId, @RouteId, 'Stop B', 2, 12.2000, 77.2000)",
            new { S1 = stop1, S2 = stop2, TenantId = tenantId, RouteId = routeId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Trips (Id, TenantId, BusId, RouteId, DriverId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, @RouteId, @DriverId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId, RouteId = routeId, DriverId = driverId });
        return (tenantId, tripId, driverId, stop1, stop2);
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> app, Guid userId, Guid tenantId, string role)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", IssueToken(userId, tenantId, role));
        return client;
    }

    [Fact]
    public async Task ConfirmArrival_out_of_order_stop_is_rejected()
    {
        var (tenantId, tripId, driverId, _, stop2) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/confirm-arrival", null);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConfirmArrival_then_Complete_advances_CurrentStopId_and_allows_the_next_stop()
    {
        var (tenantId, tripId, driverId, stop1, stop2) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var confirm1 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop1}/confirm-arrival", null);
        confirm1.IsSuccessStatusCode.Should().BeTrue();

        var completeBeforeConfirm2 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/complete", null);
        completeBeforeConfirm2.StatusCode.Should().Be(HttpStatusCode.Conflict, "stop2 was never confirmed as current");

        var complete1 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop1}/complete", null);
        complete1.IsSuccessStatusCode.Should().BeTrue();

        var confirm2 = await client.PostAsync($"/v1/staff/trips/{tripId}/stops/{stop2}/confirm-arrival", null);
        confirm2.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task SchoolArrived_sets_status_without_ending_the_trip()
    {
        var (tenantId, tripId, driverId, _, _) = await SeedLiveTripWithTwoStops();
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/school-arrived", null);
        res.IsSuccessStatusCode.Should().BeTrue();

        // Trip must still accept a subsequent action (e.g. End) — proving it wasn't closed.
        var endRes = await client.PostAsync($"/v1/staff/trips/{tripId}/end", null);
        endRes.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task SchoolArrived_on_a_drop_trip_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo, DriverStaffId) VALUES (@Id, @TenantId, 'BUS-1', @DriverId)",
                new { Id = busId, TenantId = tenantId, DriverId = driverId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Trips (Id, TenantId, BusId, DriverId, Direction, Status, StartedAt) VALUES (@Id, @TenantId, @BusId, @DriverId, 'drop', 'live', SYSUTCDATETIME())",
                new { Id = tripId, TenantId = tenantId, BusId = busId, DriverId = driverId });
        }
        await using var app = App();
        var client = AuthedClient(app, driverId, tenantId, Policies.Driver);

        var res = await client.PostAsync($"/v1/staff/trips/{tripId}/school-arrived", null);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopEndpointsTests`
Expected: FAIL — routes don't exist (404).

- [ ] **Step 3: Add the service methods**

In `src/Sms.Application/Services/Transport/ITripService.cs`, add:

```csharp
Task<ApiResult> ConfirmStopArrivalAsync(Guid tripId, Guid stopId, CancellationToken ct = default);
Task<ApiResult> CompleteStopAsync(Guid tripId, Guid stopId, CancellationToken ct = default);
Task<ApiResult> MarkSchoolArrivedAsync(Guid tripId, CancellationToken ct = default);
```

In `TripService.cs`:

```csharp
public async Task<ApiResult> ConfirmStopArrivalAsync(Guid tripId, Guid stopId, CancellationToken ct = default)
{
    if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
        return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
    if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
        return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
    if (await repo.GetCurrentStopIdAsync(tripId, ct) is { } current && current != stopId)
        return ApiResult.Fail(new Error("wrong_stop_order", "a different stop is already current"), 409);
    if (await repo.GetTripRouteIdAsync(tripId, ct) is not { } routeId)
        return ApiResult.Fail(new Error("no_route", "trip has no route"), 409);
    var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, ct);
    if (next is null || next.Id != stopId)
        return ApiResult.Fail(new Error("wrong_stop_order", "stops must be confirmed in sequence"), 409);

    await repo.ConfirmStopArrivalAsync(tid, tripId, stopId, next.Seq, clock.UtcNow, clock.UtcNow, ct);
    if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
        await fleetBroadcaster.BroadcastStopArrivedAsync(busId, tripId, stopId, next.Name, clock.UtcNow, ct);
    return ApiResult.NoContent();
}

public async Task<ApiResult> CompleteStopAsync(Guid tripId, Guid stopId, CancellationToken ct = default)
{
    if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
        return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
    if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
        return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
    if (await repo.GetCurrentStopIdAsync(tripId, ct) != stopId)
        return ApiResult.Fail(new Error("not_current_stop", "this stop is not the confirmed current stop"), 409);

    await repo.CompleteStopAsync(tid, tripId, stopId, clock.UtcNow, ct);
    if (await repo.GetBusIdAsync(tripId, ct) is { } busId && await repo.GetTripRouteIdAsync(tripId, ct) is { } routeId)
    {
        var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, ct);
        await fleetBroadcaster.BroadcastStopCompletedAsync(busId, tripId, stopId, next?.Id, next?.Name, clock.UtcNow, ct);
    }
    return ApiResult.NoContent();
}

public async Task<ApiResult> MarkSchoolArrivedAsync(Guid tripId, CancellationToken ct = default)
{
    if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
        return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
    if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
        return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
    // Note: confirm the exact method/query to check a trip's Direction and Status
    // before this call — reuse whatever GetCurrentAsync/an inline query already
    // exposes rather than adding a redundant lookup if one exists.
    if (!await repo.IsPickupTripInProgressAsync(tripId, ct))
        return ApiResult.Fail(new Error("invalid_state", "not a pickup trip in progress"), 409);

    await repo.MarkSchoolArrivedAsync(tid, tripId, clock.UtcNow, ct);
    if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
    {
        var onboard = await repo.CountBoardedAsync(tripId, ct);
        await fleetBroadcaster.BroadcastSchoolArrivedAsync(busId, tripId, clock.UtcNow, onboard, ct);
    }
    return ApiResult.NoContent();
}
```

Add the two small new repository methods this references — `TripRepository.IsPickupTripInProgressAsync(Guid tripId, ct) -> Task<bool>` (`SELECT COUNT(1) FROM dbo.Trips WHERE Id = @tripId AND Direction = 'pickup' AND Status = 'live'`), `TripRepository.MarkSchoolArrivedAsync(Guid tenantId, Guid tripId, DateTime at, ct) -> Task` (`UPDATE dbo.Trips SET Status = 'arrived' WHERE Id = @tripId AND TenantId = @tenantId`), and reuse the existing boarded-count query pattern for `CountBoardedAsync` if one already exists (checked in prior investigation: `"SELECT COUNT(*) FROM dbo.Boardings WHERE TripId = @tripId AND State = 'boarded'"` already appears in `TransportModule.cs` line ~122 — extract it into a named method if it's currently inline, or reuse the existing method name if one already wraps it).

- [ ] **Step 4: Add the controller routes**

In `src/Sms.Api/Controllers/TripController.cs`, following its exact existing style (`FromResult(await trips.XAsync(...))`):

```csharp
[HttpPost("trips/{tripId:guid}/stops/{stopId:guid}/confirm-arrival")]
public async Task<IActionResult> ConfirmStopArrival(Guid tripId, Guid stopId, CancellationToken ct) =>
    FromResult(await trips.ConfirmStopArrivalAsync(tripId, stopId, ct));

[HttpPost("trips/{tripId:guid}/stops/{stopId:guid}/complete")]
public async Task<IActionResult> CompleteStop(Guid tripId, Guid stopId, CancellationToken ct) =>
    FromResult(await trips.CompleteStopAsync(tripId, stopId, ct));

[HttpPost("trips/{tripId:guid}/school-arrived")]
public async Task<IActionResult> SchoolArrived(Guid tripId, CancellationToken ct) =>
    FromResult(await trips.MarkSchoolArrivedAsync(tripId, ct));
```

- [ ] **Step 5: Add boarding state validation**

In `TripService.UpsertBoardingAsync`, add a guard before calling `repo.UpsertBoardingAsync`:

```csharp
private static readonly string[] ValidBoardingStates = ["boarded", "absent", "dropped"];

public async Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default)
{
    if (!ValidBoardingStates.Contains(req.State))
        return ApiResult.Fail(new Error("invalid_state", $"State must be one of: {string.Join(", ", ValidBoardingStates)}"), 400);
    // ... existing body unchanged below this point
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopEndpointsTests`
Expected: PASS (4 tests). If no live SQL Server is reachable, run `dotnet build` instead.

Run: `dotnet test tests/Sms.Tests.Integration --filter Trip` (broad)
Expected: no regressions to existing boarding/trip tests (confirm no existing test relies on a `State` value outside `boarded`/`absent`/`dropped` — if one does, that test's fixture needs updating, not this validation loosened).

Note: `Task 6` (broadcaster methods `BroadcastStopArrivedAsync`/`BroadcastStopCompletedAsync`/`BroadcastSchoolArrivedAsync`) doesn't exist yet at this point — this task's own build will fail until Task 6 lands. **Do Task 6 before running this task's final build/test verification**, or stub the three broadcaster calls with `// TODO(Task 6)` comments temporarily if executing tasks out of order (not recommended — follow the plan's order).

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Api/Controllers/TripController.cs src/Sms.Application/Services/Transport/ITripService.cs src/Sms.Application/Services/Transport/TripService.cs src/Sms.Modules.Transport/TransportModule.cs tests/Sms.Tests.Integration/Transport/TripStopEndpointsTests.cs
git commit -m "feat(transport): add confirm-arrival/complete/school-arrived endpoints, standardize boarding states"
```

---

### Task 6: Broadcaster extension — stop_arrived, stop_completed, school_arrived

**Files:**
- Modify: `src/Sms.Application/Services/Transport/ITransportFleetBroadcaster.cs`
- Modify: `src/Sms.Application/Services/Transport/NoOpTransportFleetBroadcaster.cs`
- Modify: `src/Sms.Api/Services/TransportFleetBroadcaster.cs`

**Interfaces:**
- Consumes: `TransportFleetHub.BusGroup(Guid busId)` (existing, unchanged).
- Produces: three new interface methods — consumed by Task 5's `TripService` methods (already written assuming these exist).

- [ ] **Step 1: Extend the interface**

```csharp
Task BroadcastStopArrivedAsync(Guid busId, Guid tripId, Guid stopId, string stopName, DateTime confirmedAt, CancellationToken ct = default);
Task BroadcastStopCompletedAsync(Guid busId, Guid tripId, Guid stopId, Guid? nextStopId, string? nextStopName, DateTime departedAt, CancellationToken ct = default);
Task BroadcastSchoolArrivedAsync(Guid busId, Guid tripId, DateTime arrivedAt, int studentsOnboard, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in `NoOpTransportFleetBroadcaster`**

```csharp
public Task BroadcastStopArrivedAsync(Guid busId, Guid tripId, Guid stopId, string stopName, DateTime confirmedAt, CancellationToken ct = default) => Task.CompletedTask;
public Task BroadcastStopCompletedAsync(Guid busId, Guid tripId, Guid stopId, Guid? nextStopId, string? nextStopName, DateTime departedAt, CancellationToken ct = default) => Task.CompletedTask;
public Task BroadcastSchoolArrivedAsync(Guid busId, Guid tripId, DateTime arrivedAt, int studentsOnboard, CancellationToken ct = default) => Task.CompletedTask;
```

- [ ] **Step 3: Implement in `TransportFleetBroadcaster`**

```csharp
public async Task BroadcastStopArrivedAsync(Guid busId, Guid tripId, Guid stopId, string stopName, DateTime confirmedAt, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("stop_arrived",
        new { busId, tripId, stopId, stopName, confirmedAt }, ct);

public async Task BroadcastStopCompletedAsync(Guid busId, Guid tripId, Guid stopId, Guid? nextStopId, string? nextStopName, DateTime departedAt, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("stop_completed",
        new { busId, tripId, stopId, departedAt, nextStopId, nextStopName }, ct);

public async Task BroadcastSchoolArrivedAsync(Guid busId, Guid tripId, DateTime arrivedAt, int studentsOnboard, CancellationToken ct = default) =>
    await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("school_arrived",
        new { busId, tripId, arrivedAt, studentsOnboard }, ct);
```

- [ ] **Step 4: Check for any other `ITransportFleetBroadcaster` implementer**

Run: `grep -rln "ITransportFleetBroadcaster" --include=*.cs tests/ src/`

The prior feature's Task 4 discovered a `SpyFleetBroadcaster` test double in `tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs` that also implements this interface — add the same three no-op/recording stub methods there, matching its existing style, or the whole solution won't build.

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build` (whole solution)
Expected: 0 errors — this specifically confirms every implementer (including the test spy) was updated.

- [ ] **Step 6: Run Task 5's full test suite now that this task unblocks it**

Run: `dotnet test tests/Sms.Tests.Integration --filter TripStopEndpointsTests`
Expected: PASS (4 tests) — this was blocked pending this task per Task 5's Step 6 note.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Application/Services/Transport/ITransportFleetBroadcaster.cs src/Sms.Application/Services/Transport/NoOpTransportFleetBroadcaster.cs src/Sms.Api/Services/TransportFleetBroadcaster.cs tests/Sms.Tests.Integration/Transport/TripBroadcastTests.cs
git commit -m "feat(transport): broadcast stop_arrived/stop_completed/school_arrived events"
```

---

### Task 7: Full verification pass

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `export Jwt__SigningKey="compose-dev-signing-key-at-least-32-bytes!!" && dotnet test tests/Sms.Tests.Unit && dotnet test tests/Sms.Tests.Integration`
Expected: all tests pass, including every test added in Tasks 1-6. If a live SQL Server is unreachable, run `dotnet test tests/Sms.Tests.Unit` (no DB dependency) and confirm it's green, and separately confirm `dotnet build tests/Sms.Tests.Integration` compiles cleanly — note the inability to execute integration tests as a real limitation when reporting completion.

- [ ] **Step 3: Confirm the acceptance-criteria scenario end-to-end at the API level**

Re-read the spec's scenario (start trip → confirm arrival at stop 1 → mark students boarded → complete stop 1 → confirm arrival at stop 2 → ... → school-arrived → end, then a new drop trip → confirm/complete drop stops → end) and confirm each step has a passing integration test somewhere in Tasks 3-5's suites. List any step that has no direct test coverage.

- [ ] **Step 4: Confirm no regressions to existing transport/trip endpoints**

Run: `dotnet test tests/Sms.Tests.Integration --filter Transport`
Expected: all pre-existing transport tests (fleet, bus position, parent transport, trip lifecycle, the prior plan's authorization/hub/offline-sweep tests) still pass unchanged.

- [ ] **Step 5: Final commit (if anything was fixed during verification)**

```bash
git add -A
git commit -m "fix(transport): address issues found during full verification pass"
```

(Skip this step if verification found nothing to fix.)
