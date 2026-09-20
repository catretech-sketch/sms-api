# Teacher+Principal App — Phase 3: New-Data Features (timetable, calendar, library, assignments) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the four teacher/principal screens that need brand-new tables — timetable, calendar,
library, and (class-level) assignments — each with GET (read) and POST (create), RLS, and tests.

**Architecture:** All four resources live in the **Academics module** (new contract + repository files
per resource; endpoints added to `AcademicsModule.cs`). Each gets a FluentMigrator migration creating its
table + an `rls.*` tenant policy + an inline `CREATE OR ALTER PROCEDURE` insert proc (writes go through
procs; reads use parameterized `QueryInlineAsync`). Swagger mapping exposes them in the Teacher doc.

**Tech Stack:** .NET 10 minimal APIs, Dapper, FluentMigrator, SQL Server (RLS per tenant), ASP.NET
authorization policies, xUnit + FluentAssertions.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-21-teacher-principal-app-complete-design.md` (see the Phase-3
  amendment note at the top).
- Wire **snake_case**; success wraps in `DataEnvelope<T>`; create returns `201` with `DataEnvelope<T>`;
  errors via `Results.Json(ErrorEnvelope.From(new Error(code, message)), statusCode: N)`.
- Authorization (registered Phase 1): `AuthorizationPolicies.TeacherApp` (teacher/principal/admin),
  `Policies.Principal` (principal/admin). **GET** timetable/calendar/library/assignments → `TeacherApp`.
  **POST** timetable/calendar/library → `Policies.Principal`. **POST** assignments → `TeacherApp`.
  `student.parent` → 403 on all.
- New tables follow the RLS pattern of `db/Sms.Migrations/M0021_Geofence_Tables.cs`: GUID PK
  `NewSequentialId`, `TenantId` NOT NULL, a covering index, and
  `CREATE SECURITY POLICY rls.<Table>TenantPolicy ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId)
  ON dbo.<Table>, ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.<Table> AFTER INSERT WITH (STATE = ON);`
  dropped in `Down()`. Migrations are **M0040–M0043** (head is M0039) and must keep
  `MigrationIdempotenceTests` green.
- Insert procs follow `M0039_Procs_ExamPaper_Edit.cs` style: `CREATE OR ALTER PROCEDURE` in `Up()`,
  `DROP PROCEDURE IF EXISTS` in `Down()`; the proc inserts (PK via `NEWSEQUENTIALID()` default by omitting
  Id) and `SELECT`s the new row back. Tenant comes from the RLS session context — the proc takes `@TenantId`
  explicitly (the API passes `tenant.TenantId`), matching existing `*_Create` procs.
- Reads parameterized via `QueryInlineAsync`; RLS scopes by tenant (no explicit `TenantId` WHERE).
- "today"/"now" via `IClock` (`UtcNow`).
- Repos extend `BaseRepository` (`QueryInlineAsync`, `QuerySingleProcAsync`).
- Known unrelated pre-existing failing test `CatreOpsTests.Onboarding_...checklist` — ignore it.
- Test infra: see `.superpowers/sdd/test-infra-cheatsheet.md` (the real `SqlServerFixture`/`App()`/canonical-role
  token pattern; the plan's `ApiFactory.*` helper names do not exist). Seed via the new POST endpoints.
- Commit messages: conventional; **no** `Co-Authored-By` line.

## Confirmed schema facts
- `dbo.Classes(Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId)`.
- `dbo.Students(... Grade, Section ...)` — class membership is Grade+Section (no ClassId).
- `dbo.Homework(... AssignmentId, Status default 'todo' ...)` — used for assignment `submissions_count`.
- Migration head is **M0039**; existing tenant predicate is `rls.fn_tenant_predicate(TenantId)`.

---

## File Structure
- `db/Sms.Migrations/M0040_Timetable_Tables.cs` / `M0041_Calendar_Tables.cs` /
  `M0042_Library_Tables.cs` / `M0043_Assignments_Tables.cs` — **new** (table + RLS + insert proc each).
- `src/Sms.Modules.Academics/Contracts/ScheduleContracts.cs` — **new** (timetable + calendar + library DTOs).
- `src/Sms.Modules.Academics/Contracts/AssignmentContracts.cs` — **new**.
- `src/Sms.Modules.Academics/Data/TimetableRepository.cs`, `CalendarRepository.cs`, `LibraryRepository.cs`,
  `AssignmentRepository.cs` — **new**.
- `src/Sms.Modules.Academics/AcademicsModule.cs` — **modify** (register the 4 repos in `AddAcademicsModule`;
  add the 8 endpoints in `MapAcademicsModule`).
- `src/Sms.Api/Swagger/ApiAudienceMap.cs` + `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs` —
  **modify** (Task 5).
- Tests: `tests/Sms.Tests.Integration/Academics/{Timetable,Calendar,Library,Assignment}Tests.cs` — **new**.

---

### Task 1: Timetable — `GET` + `POST /v1/timetable`

**Files:** Create migration `M0040_Timetable_Tables.cs`; `Contracts/ScheduleContracts.cs` (timetable part);
`Data/TimetableRepository.cs`; modify `AcademicsModule.cs`; test `Academics/TimetableTests.cs`.

**Interfaces:**
- Produces: `TimetableSlotResponse(Guid Id, Guid TenantId, string Day, int Period, string? Subject,
  Guid? ClassId, string? ClassName, string? Room, string? StartTime, string? EndTime)`;
  `CreateTimetableSlotRequest(string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
  string? Room, string? StartTime, string? EndTime)`; `TimetableRepository.ListAsync(CancellationToken)`,
  `TimetableRepository.CreateAsync(Guid tenantId, CreateTimetableSlotRequest, CancellationToken)`.

- [ ] **Step 1: Migration**

`db/Sms.Migrations/M0040_Timetable_Tables.cs`:

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(40, "Timetable: TimetableSlots table + tenant RLS + insert proc")]
public sealed class M0040_Timetable_Tables : Migration
{
    public override void Up()
    {
        Create.Table("TimetableSlots")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Day").AsString(3).NotNullable()
            .WithColumn("Period").AsInt32().NotNullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("ClassId").AsGuid().Nullable()
            .WithColumn("ClassName").AsString(80).Nullable()
            .WithColumn("Room").AsString(40).Nullable()
            .WithColumn("StartTime").AsString(10).Nullable()
            .WithColumn("EndTime").AsString(10).Nullable();
        Create.Index("IX_TimetableSlots_Tenant").OnTable("TimetableSlots").OnColumn("TenantId").Ascending();

        Execute.Sql(@"CREATE SECURITY POLICY rls.TimetableSlotsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TimetableSlots,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TimetableSlots AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Create
    @TenantId uniqueidentifier, @Day nvarchar(3), @Period int, @Subject nvarchar(80) = NULL,
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL, @Room nvarchar(40) = NULL,
    @StartTime nvarchar(10) = NULL, @EndTime nvarchar(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Day, @Period, @Subject, @ClassId, @ClassName, @Room, @StartTime, @EndTime);
    SELECT Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime
    FROM dbo.TimetableSlots WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TimetableSlot_Create;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TimetableSlotsTenantPolicy;");
        Delete.Table("TimetableSlots");
    }
}
```

- [ ] **Step 2: Contracts** — create `src/Sms.Modules.Academics/Contracts/ScheduleContracts.cs`:

```csharp
namespace Sms.Modules.Academics.Contracts;

public sealed record TimetableSlotResponse(
    Guid Id, Guid TenantId, string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
    string? Room, string? StartTime, string? EndTime);
public sealed record CreateTimetableSlotRequest(
    string Day, int Period, string? Subject, Guid? ClassId, string? ClassName, string? Room,
    string? StartTime, string? EndTime);
```

- [ ] **Step 3: Repository** — `src/Sms.Modules.Academics/Data/TimetableRepository.cs`:

```csharp
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class TimetableRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime";

    public Task<IReadOnlyList<TimetableSlotResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>(
            $"SELECT {Cols} FROM dbo.TimetableSlots ORDER BY [Day], Period", null, ct);

    public Task<TimetableSlotResponse?> CreateAsync(Guid tenantId, CreateTimetableSlotRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TimetableSlotResponse>("dbo.TimetableSlot_Create", new
        {
            TenantId = tenantId, r.Day, r.Period, r.Subject, r.ClassId, r.ClassName, r.Room, r.StartTime, r.EndTime
        }, ct);
}
```

- [ ] **Step 4: Failing test** — `tests/Sms.Tests.Integration/Academics/TimetableTests.cs`: using the
  cheatsheet pattern, POST a slot as a principal (assert 201 + fields), GET as a teacher (assert the slot
  is listed), assert POST as `student.parent` → 403 and POST as plain teacher → 403 (principal-gated
  create), and GET as `student.parent` → 403. Run:
  `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~TimetableTests` → FAIL.

- [ ] **Step 5: Register + map** — in `AcademicsModule.cs`:
  - `AddAcademicsModule`: add `services.AddScoped<TimetableRepository>();`.
  - `MapAcademicsModule` (group `g` = `/v1`, add `using Sms.Shared.Kernel.Authz;` if missing):

```csharp
        g.MapGet("/timetable", async (TimetableRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<TimetableSlotResponse>>(await repo.ListAsync())))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/timetable", async (CreateTimetableSlotRequest req, TimetableRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<TimetableSlotResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);
```

  (`Forbidden` is the existing private helper in this file.)

- [ ] **Step 6: Run tests → PASS.** `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~TimetableTests`.
  Then run full `tests/Sms.Tests.Integration` (only the known onboarding failure may remain) + the
  `MigrationIdempotenceTests` filter to confirm M0040 is idempotent.

- [ ] **Step 7: Commit**

```bash
git add db/Sms.Migrations/M0040_Timetable_Tables.cs src/Sms.Modules.Academics/Contracts/ScheduleContracts.cs src/Sms.Modules.Academics/Data/TimetableRepository.cs src/Sms.Modules.Academics/AcademicsModule.cs tests/Sms.Tests.Integration/Academics/TimetableTests.cs
git commit -m "feat(academics): timetable GET (teacher) + POST (principal), M0040"
```

---

### Task 2: Calendar — `GET` + `POST /v1/calendar`

Mirror Task 1 exactly, substituting the calendar shape. **Files:** `M0041_Calendar_Tables.cs`; add to
`ScheduleContracts.cs`; `Data/CalendarRepository.cs`; modify `AcademicsModule.cs`; test `CalendarTests.cs`.

**Interfaces:** `CalendarEventResponse(Guid Id, Guid TenantId, string Title, DateTime Date, string? Time,
string Type, string? Description)`; `CreateCalendarEventRequest(string Title, DateTime Date, string? Time,
string Type, string? Description)`; `CalendarRepository.ListAsync`, `.CreateAsync(Guid tenantId, ...)`.

- [ ] **Step 1: Migration `M0041_Calendar_Tables.cs`** — same structure as M0040 with table `CalendarEvents`:
  columns `Id`(PK NewSequentialId), `TenantId`(guid NN), `Title`(nvarchar 200 NN), `Date`(date NN),
  `Time`(nvarchar 10 null), `Type`(nvarchar 20 NN), `Description`(nvarchar max null); index on `TenantId`;
  `rls.CalendarEventsTenantPolicy`; proc `dbo.CalendarEvent_Create(@TenantId, @Title, @Date, @Time, @Type,
  @Description)` that inserts and SELECTs the row back (columns `Id, TenantId, Title, [Date], Time, Type, Description`).
  `Down()` drops proc, policy, table.

- [ ] **Step 2: Contract** — append to `ScheduleContracts.cs` the two records above.

- [ ] **Step 3: Repository `CalendarRepository.cs`** — `Cols = "Id, TenantId, Title, [Date], Time, Type, Description"`;
  `ListAsync` = `SELECT {Cols} FROM dbo.CalendarEvents ORDER BY [Date], Time`; `CreateAsync` calls
  `dbo.CalendarEvent_Create` with `{ TenantId = tenantId, r.Title, r.Date, r.Time, r.Type, r.Description }`.

- [ ] **Step 4: Failing test `CalendarTests.cs`** — POST event as principal → 201; GET as teacher → listed;
  POST as teacher and as student → 403; GET as student → 403. Run filter → FAIL.

- [ ] **Step 5: Register + map** in `AcademicsModule.cs`: `services.AddScoped<CalendarRepository>();` and:

```csharp
        g.MapGet("/calendar", async (CalendarRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<CalendarEventResponse>>(await repo.ListAsync())))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/calendar", async (CreateCalendarEventRequest req, CalendarRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<CalendarEventResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);
```

- [ ] **Step 6: Run filter → PASS;** full suite + idempotence check.
- [ ] **Step 7: Commit** — `feat(academics): calendar GET (teacher) + POST (principal), M0041`.

---

### Task 3: Library — `GET` + `POST /v1/library`

Mirror Task 1; library has a derived `overdue` status on read. **Files:** `M0042_Library_Tables.cs`; add to
`ScheduleContracts.cs`; `Data/LibraryRepository.cs`; modify `AcademicsModule.cs`; test `LibraryTests.cs`.

**Interfaces:** `LibraryBookResponse(Guid Id, Guid TenantId, string Title, string Author, string? Subject,
string? IssuedTo, DateTime? DueDate, string Status)`; `CreateLibraryBookRequest(string Title, string Author,
string? Subject, string? IssuedTo, DateTime? DueDate, string? Status)`; `LibraryRepository.ListAsync(DateTime today, ...)`,
`.CreateAsync(Guid tenantId, ...)`.

- [ ] **Step 1: Migration `M0042_Library_Tables.cs`** — table `LibraryBooks`: `Id`, `TenantId`,
  `Title`(nvarchar 200 NN), `Author`(nvarchar 120 NN), `Subject`(nvarchar 80 null), `IssuedTo`(nvarchar 120 null),
  `DueDate`(date null), `Status`(nvarchar 20 NN default `'available'`); index on `TenantId`;
  `rls.LibraryBooksTenantPolicy`; proc `dbo.LibraryBook_Create(@TenantId, @Title, @Author, @Subject,
  @IssuedTo, @DueDate, @Status nvarchar(20) = 'available')` insert + SELECT back. `Down()` drops all.

- [ ] **Step 2: Contract** — append the two records to `ScheduleContracts.cs`.

- [ ] **Step 3: Repository `LibraryRepository.cs`** — derive `overdue` in SQL on read:

```csharp
    public Task<IReadOnlyList<LibraryBookResponse>> ListAsync(DateTime today, CancellationToken ct = default) =>
        QueryInlineAsync<LibraryBookResponse>(
            @"SELECT Id, TenantId, Title, Author, Subject, IssuedTo, DueDate,
                     CASE WHEN Status = 'issued' AND DueDate IS NOT NULL AND DueDate < @today
                          THEN 'overdue' ELSE Status END AS Status
              FROM dbo.LibraryBooks ORDER BY Title", new { today = today.Date }, ct);

    public Task<LibraryBookResponse?> CreateAsync(Guid tenantId, CreateLibraryBookRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<LibraryBookResponse>("dbo.LibraryBook_Create", new
        { TenantId = tenantId, r.Title, r.Author, r.Subject, r.IssuedTo, DueDate = r.DueDate, Status = r.Status ?? "available" }, ct);
```

- [ ] **Step 4: Failing test `LibraryTests.cs`** — POST a book with `status='issued'` and a past `due_date`
  as principal → 201; GET as teacher → the book's status reads `overdue` (derived); POST an `available` book
  and assert it stays `available`; POST as teacher/student → 403; GET as student → 403. Filter → FAIL.

- [ ] **Step 5: Register + map** — `services.AddScoped<LibraryRepository>();` and:

```csharp
        g.MapGet("/library", async (LibraryRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<LibraryBookResponse>>(await repo.ListAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/library", async (CreateLibraryBookRequest req, LibraryRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<LibraryBookResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);
```

  (Add `using Sms.Shared.Kernel.Time;` for `IClock` if not already imported.)

- [ ] **Step 6: Run filter → PASS;** full suite + idempotence.
- [ ] **Step 7: Commit** — `feat(academics): library GET (teacher) + POST (principal), M0042`.

---

### Task 4: Assignments — `GET` + `POST /v1/assignments`

**Files:** `M0043_Assignments_Tables.cs`; `Contracts/AssignmentContracts.cs`; `Data/AssignmentRepository.cs`;
modify `AcademicsModule.cs`; test `AssignmentTests.cs`.

**Interfaces:** `AssignmentResponse(Guid Id, Guid TenantId, string Title, Guid? ClassId, string? ClassName,
string? Subject, DateTime? DueDate, int SubmissionsCount, int TotalStudents, string Status, string? Description,
string? ImageUri)`; `CreateAssignmentRequest(string Title, Guid? ClassId, string? ClassName, string? Subject,
DateTime? DueDate, string? Description, string? ImageUri)`; `AssignmentRepository.ListAsync(DateTime today, ...)`,
`.CreateAsync(Guid tenantId, ...)`.

- [ ] **Step 1: Migration `M0043_Assignments_Tables.cs`** — table `Assignments`: `Id`, `TenantId`,
  `Title`(nvarchar 200 NN), `ClassId`(guid null), `ClassName`(nvarchar 80 null), `Subject`(nvarchar 80 null),
  `DueDate`(date null), `Description`(nvarchar max null), `ImageUri`(nvarchar 400 null),
  `Status`(nvarchar 20 NN default `'active'`); index on `TenantId`; `rls.AssignmentsTenantPolicy`;
  proc `dbo.Assignment_Create(@TenantId, @Title, @ClassId, @ClassName, @Subject, @DueDate, @Description, @ImageUri)`
  that inserts (Status defaults 'active') and SELECTs back the raw columns
  `Id, TenantId, Title, ClassId, ClassName, Subject, DueDate, Description, ImageUri, Status`.

- [ ] **Step 2: Contracts `AssignmentContracts.cs`** — the two records above, plus a private row record for
  the read query:

```csharp
namespace Sms.Modules.Academics.Contracts;

public sealed record AssignmentResponse(
    Guid Id, Guid TenantId, string Title, Guid? ClassId, string? ClassName, string? Subject, DateTime? DueDate,
    int SubmissionsCount, int TotalStudents, string Status, string? Description, string? ImageUri);
public sealed record CreateAssignmentRequest(
    string Title, Guid? ClassId, string? ClassName, string? Subject, DateTime? DueDate, string? Description, string? ImageUri);
```

- [ ] **Step 3: Repository `AssignmentRepository.cs`** — read computes `SubmissionsCount` (from `Homework`),
  `TotalStudents` (class roster via Grade+Section), and derives `Status` in C#:

```csharp
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class AssignmentRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record Row(
        Guid Id, Guid TenantId, string Title, Guid? ClassId, string? ClassName, string? Subject,
        DateTime? DueDate, int SubmissionsCount, int TotalStudents, string RawStatus, string? Description, string? ImageUri);

    public async Task<IReadOnlyList<AssignmentResponse>> ListAsync(DateTime today, CancellationToken ct = default)
    {
        var d = today.Date;
        var rows = await QueryInlineAsync<Row>(@"
SELECT a.Id, a.TenantId, a.Title, a.ClassId, a.ClassName, a.Subject, a.DueDate,
  (SELECT COUNT(*) FROM dbo.Homework h WHERE h.AssignmentId = a.Id AND h.Status IN ('done','submitted')) AS SubmissionsCount,
  ISNULL((SELECT COUNT(*) FROM dbo.Students s JOIN dbo.Classes c
          ON c.Id = a.ClassId AND s.Grade = c.Grade AND s.Section = c.Section), 0) AS TotalStudents,
  a.Status AS RawStatus, a.Description, a.ImageUri
FROM dbo.Assignments a ORDER BY a.DueDate", null, ct);

        return rows.Select(r => new AssignmentResponse(
            r.Id, r.TenantId, r.Title, r.ClassId, r.ClassName, r.Subject, r.DueDate,
            r.SubmissionsCount, r.TotalStudents, DeriveStatus(r.RawStatus, r.DueDate, d), r.Description, r.ImageUri)).ToList();
    }

    public Task<AssignmentResponse?> CreateAsync(Guid tenantId, CreateAssignmentRequest r, CancellationToken ct = default) =>
        // Create returns the row with computed fields = 0/derived; re-list is not needed for the 201 body.
        QuerySingleProcAsync<AssignmentResponse>("dbo.Assignment_Create", new
        {
            TenantId = tenantId, r.Title, r.ClassId, r.ClassName, r.Subject, r.DueDate, r.Description, r.ImageUri
        }, ct);

    // RawStatus 'closed' wins; else overdue if past due; else due_soon within 3 days; else active.
    private static string DeriveStatus(string raw, DateTime? due, DateTime today)
    {
        if (string.Equals(raw, "closed", StringComparison.OrdinalIgnoreCase)) return "closed";
        if (due is not { } d) return "active";
        if (d.Date < today) return "overdue";
        if (d.Date <= today.AddDays(3)) return "due_soon";
        return "active";
    }
}
```

  NOTE: the create proc's SELECT-back must return columns aliased to match `AssignmentResponse` —
  `SubmissionsCount`/`TotalStudents` as literal `0` and `Status` as the stored value — so
  `QuerySingleProcAsync<AssignmentResponse>` maps cleanly. The create proc SELECT:
  `SELECT Id, TenantId, Title, ClassId, ClassName, Subject, DueDate, 0 AS SubmissionsCount, 0 AS TotalStudents, Status, Description, ImageUri FROM dbo.Assignments WHERE Id = (SELECT Id FROM @ins);`
  (So a freshly created assignment reports 0 submissions/0 students in its 201 body; the GET list computes
  the real counts.)

- [ ] **Step 4: Failing test `AssignmentTests.cs`** — seed a class (Grade 7/B) + 2 students in it; POST an
  assignment for that class with a `due_date` 1 day in the past as a TEACHER → 201; GET as teacher → the
  assignment shows `total_students = 2`, `submissions_count = 0`, `status = "overdue"`. POST a second
  assignment due 10 days out → status `active`; one due tomorrow → `due_soon`. Assert `student.parent` → 403
  on both GET and POST. (Teacher CAN create — assignments POST is teacher-app.) Filter → FAIL.

- [ ] **Step 5: Register + map** — `services.AddScoped<AssignmentRepository>();` and:

```csharp
        g.MapGet("/assignments", async (AssignmentRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<AssignmentResponse>>(await repo.ListAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/assignments", async (CreateAssignmentRequest req, AssignmentRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<AssignmentResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);
```

- [ ] **Step 6: Run filter → PASS;** full suite + idempotence.
- [ ] **Step 7: Commit** — `feat(academics): assignments GET + POST (teacher), M0043`.

---

### Task 5: Swagger audience mapping for the new routes

**Files:** `src/Sms.Api/Swagger/ApiAudienceMap.cs`; `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs`.

- [ ] **Step 1: Failing assertions** — add to `SwaggerPerAppTests` that the `teacher` doc's paths contain
  `/v1/timetable`, `/v1/calendar`, `/v1/library`, `/v1/assignments`. Filter
  `~SwaggerPerAppTests` → FAIL.

- [ ] **Step 2: Map** — in `ApiAudienceMap.cs` `Rules`, add (calendar/library are cross-app per the
  contract — calendar is shared, library shared; but for THIS phase map to Teacher; broaden later if the
  student app needs them):
  - `("v1/timetable", [Teacher])`
  - `("v1/calendar", [Teacher, Student])`  (calendar is a shared screen per the contract §3)
  - `("v1/library", [Teacher])`
  - `("v1/assignments", [Teacher, Student])`  (assignments are teacher+student per contract §3)
  Place them among the school-scoped rules; none collide with an existing more-specific prefix.

- [ ] **Step 3: Run filter → PASS.**
- [ ] **Step 4: Commit** — `feat(api): expose timetable/calendar/library/assignments in Swagger docs`.

---

## Self-Review

- **Spec coverage:** §5.3 timetable (Task 1), calendar (Task 2), library (Task 3) + the amendment's
  assignments (Task 4); §6 Swagger (Task 5). Each new write endpoint validates tenant context (`Forbidden`
  when absent) and is policy-gated per the amendment (GET=TeacherApp; timetable/calendar/library POST=Principal;
  assignments POST=TeacherApp). Migrations M0040–M0043, each RLS-policied + idempotent (`CREATE OR ALTER`
  procs, `DROP IF EXISTS` + `Delete.Table` down).
- **Placeholder scan:** none — Tasks 1, 3, 4 carry complete migration/contract/repo/endpoint code; Tasks 2
  and 5 are spelled out field-by-field and reference Task 1's verbatim structure (acceptable: same file
  family, the records/SQL differ only in columns which are enumerated). The implementer for Task 2 should
  copy Task 1's migration/repo shape and swap the columns listed in its steps.
- **Type consistency:** `TimetableSlotResponse`/`CreateTimetableSlotRequest` + `TimetableRepository.ListAsync/CreateAsync`;
  `CalendarEventResponse`/`CreateCalendarEventRequest`; `LibraryBookResponse`/`CreateLibraryBookRequest` +
  `ListAsync(DateTime today,...)`; `AssignmentResponse`/`CreateAssignmentRequest` + `DeriveStatus`. All four
  repos registered in `AddAcademicsModule` and mapped in `MapAcademicsModule`.

## Next phases
- **Phase 4** — bus-duty teacher view (Transport reuse + new assignment table).
- **Phase 5** — final Swagger/test sweep + full integration run + (optional) the deferred follow-ups
  (check-in query pushdown, exam-paper GET/POST gating, onboarding test).
