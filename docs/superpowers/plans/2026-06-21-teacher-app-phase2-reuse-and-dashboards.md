# Teacher+Principal App — Phase 2: Reuse-Data Endpoints + Dashboards + Swagger — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Teacher+Principal endpoints that reuse existing tables — class roster, exam-paper
edit/delete, check-in history/summary, teacher dashboard, principal overview/attendance — and expose the
principal + new routes in the Teacher Swagger doc.

**Architecture:** Add endpoints to the owning modules (Sis, Academics, Attendance) and revive the empty
`Sms.Modules.Reporting` for cross-cutting dashboard aggregations. Reads use `QueryInlineAsync`; the one
write pair (exam-paper update/delete) adds stored procs via migration M0035. Pagination uses the Phase-1
`Cursor` codec + existing `PageRequest`/`CursorPage<T>`.

**Tech Stack:** .NET 10 minimal APIs, Dapper, FluentMigrator, SQL Server (RLS per tenant), ASP.NET
authorization policies, xUnit + FluentAssertions.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-21-teacher-principal-app-complete-design.md` (see the post-Phase-1
  amendment note at the top — assignments moved to Phase 3; `classes/{id}/students` joins on Grade+Section).
- Wire format **snake_case**; success wraps in `DataEnvelope<T>` or `CursorPage<T>`; errors via
  `Results.Json(ErrorEnvelope.From(new Error(code, message)), statusCode: N)` (type
  `Sms.Shared.Kernel.Results.Error`).
- Authorization policies (registered Phase 1 via `AddSmsAuthorization`): `AuthorizationPolicies.TeacherApp`
  (`"teacher.app"` = teacher/principal/admin), `Policies.Principal` (`"school.principal"` =
  principal/admin). Tenant scoping is automatic via RLS + `ITenantContext`.
- Pagination: `PageRequest(int Limit = 50, string? Cursor = null)` with `SafeLimit` (1..200 else 50);
  `CursorPage<T>(IReadOnlyList<T> Data, string? NextCursor)`; `Cursor.Encode(string)`/`Cursor.Decode(string?)`
  (returns null on null/empty/malformed). All in `Sms.Shared.Kernel.Http`.
- Repositories extend `BaseRepository`: `QueryInlineAsync<T>(sql, args, ct)` for reads,
  `QueryProcAsync`/`QuerySingleProcAsync`/`ExecuteProcAsync` for procs. Never string-concat SQL params.
- "Today"/"now" via injected `IClock` (`Sms.Shared.Kernel.Time.IClock`, `UtcNow`).
- **Present = roll-call status `present` OR `late`** for all KPI math.
- Known unrelated pre-existing failing test: `CatreOpsTests.Onboarding_...checklist` — ignore it.
- Commit messages: conventional style; **do not** add any `Co-Authored-By` line.

## Confirmed schema facts (from the data-layer audit — do not re-derive)

- `dbo.Students(Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName,
  GuardianPhone, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address)` — **no
  ClassId**. `StudentRepository.Cols` already lists the response columns.
- `dbo.Classes(Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId)`.
- `dbo.ExamPapers(Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, Date, StartTime, DurationMin,
  MaxMarks, Room, Invigilator1, Invigilator2, Status default 'upcoming')`. `ExamRepository.PaperCols`
  exists. No update/delete proc yet.
- `dbo.CheckIns(Id, TenantId, UserId, Kind('in'|'out'), At, Lat, Lng, AccuracyMeters, DistanceMeters,
  Verified)`. `CheckInRepository.GetTodayAsync` + private `CheckInRow` + `ToEvent` exist.
- `dbo.Teachers(Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher, Phone,
  Email, Exp, Rating, AttendancePct, Result, Load, Status default 'active', AvatarHue, Top)`.
- `dbo.Users(Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status)`.
- `dbo.LeaveRequests(... Status default 'pending' ...)`, indexed on `(TenantId, Status)`.
- `dbo.Homework(... Status default 'todo', DueDate ...)`; `dbo.Exams`/`dbo.ExamPapers` for upcoming.
- `Sms.Modules.Reporting` is an empty shell that already references `Sms.Shared.Kernel`, and `Sms.Api`
  already references it — so new files there need no `.csproj` edits.

---

## File Structure

- `src/Sms.Modules.Sis/Data/StudentRepository.cs` — **modify**: add `ListByClassPagedAsync`.
- `src/Sms.Modules.Sis/SisModule.cs` — **modify**: add `GET /classes/{classId}/students`.
- `db/Sms.Migrations/M0035_Procs_ExamPaper_Edit.cs` — **new**: `ExamPaper_Update` + `ExamPaper_Delete` procs.
- `src/Sms.Modules.Academics/Contracts/ExamContracts.cs` — **modify**: add `UpdateExamPaperRequest`.
- `src/Sms.Modules.Academics/Data/ExamRepository.cs` — **modify**: add `UpdateExamPaperAsync`,`DeleteExamPaperAsync`.
- `src/Sms.Modules.Academics/AcademicsModule.cs` — **modify**: add PATCH + DELETE `/exam-papers/{id}`.
- `src/Sms.Modules.Attendance/AttendanceModule.cs` — **modify**: add history + summary repo methods + routes + DTO.
- `src/Sms.Modules.Reporting/ReportingModule.cs` — **new**: DI + endpoint mapping.
- `src/Sms.Modules.Reporting/Contracts/ReportingContracts.cs` — **new**: dashboard + principal DTOs.
- `src/Sms.Modules.Reporting/Data/ReportingRepository.cs` — **new**: aggregation queries.
- `src/Sms.Api/Program.cs` — **modify**: `AddReportingModule()` + `MapReportingModule(app)`.
- `src/Sms.Api/Swagger/ApiAudienceMap.cs` — **modify**: map approvals + principal + new routes.
- Tests under `tests/Sms.Tests.Integration/{Sis,Academics,Attendance,Reporting,Swagger}/`.

---

### Task 1: `GET /v1/classes/{classId}/students` (paginated)

**Files:**
- Modify: `src/Sms.Modules.Sis/Data/StudentRepository.cs`
- Modify: `src/Sms.Modules.Sis/SisModule.cs:30-47`
- Test: `tests/Sms.Tests.Integration/Sis/ClassStudentsTests.cs`

**Interfaces:**
- Consumes: `Cursor.Encode/Decode`, `PageRequest`, `CursorPage<T>`, `AuthorizationPolicies.TeacherApp`.
- Produces: `StudentRepository.ListByClassPagedAsync(Guid classId, int limit, string? cursor, CancellationToken)`
  returning `(IReadOnlyList<StudentResponse> Rows, string? NextCursor)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Integration.Sis;

public class ClassStudentsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ClassStudentsTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Lists_students_of_a_class_by_grade_and_section()
    {
        // Seed a tenant with a class (Grade 5 / A) and 2 matching + 1 non-matching student.
        var (client, tenantId) = await _f.NewTenantClientAsync(roles: new[] { Policies.Teacher });
        var classId = await _f.SeedClassAsync(tenantId, grade: "5", section: "A");
        await _f.SeedStudentAsync(tenantId, name: "Asha", grade: "5", section: "A");
        await _f.SeedStudentAsync(tenantId, name: "Bims", grade: "5", section: "A");
        await _f.SeedStudentAsync(tenantId, name: "Zed",  grade: "6", section: "B");

        var resp = await client.GetAsync($"/v1/classes/{classId}/students?limit=50");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var names = json.GetProperty("data").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        names.Should().BeEquivalentTo(new[] { "Asha", "Bims" });
        json.GetProperty("next_cursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Paginates_with_cursor()
    {
        var (client, tenantId) = await _f.NewTenantClientAsync(roles: new[] { Policies.Teacher });
        var classId = await _f.SeedClassAsync(tenantId, grade: "5", section: "A");
        foreach (var n in new[] { "A1", "A2", "A3" })
            await _f.SeedStudentAsync(tenantId, name: n, grade: "5", section: "A");

        var page1 = await (await client.GetAsync($"/v1/classes/{classId}/students?limit=2"))
            .Content.ReadFromJsonAsync<JsonElement>();
        page1.GetProperty("data").GetArrayLength().Should().Be(2);
        var cursor = page1.GetProperty("next_cursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        var page2 = await (await client.GetAsync($"/v1/classes/{classId}/students?limit=2&cursor={cursor}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        page2.GetProperty("data").GetArrayLength().Should().Be(1);
        page2.GetProperty("data")[0].GetProperty("name").GetString().Should().Be("A3");
    }

    [Fact]
    public async Task Student_role_is_forbidden()
    {
        var (client, tenantId) = await _f.NewTenantClientAsync(roles: new[] { Policies.StudentOrParent });
        var classId = await _f.SeedClassAsync(tenantId, grade: "5", section: "A");
        (await client.GetAsync($"/v1/classes/{classId}/students")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}
```

> **Test fixture note:** `ApiFactory.NewTenantClientAsync`/`SeedClassAsync`/`SeedStudentAsync` may not
> exist as shared helpers. FIRST read an existing integration test (e.g.
> `tests/Sms.Tests.Integration/Staffing/LeaveApprovalsTests.cs`,
> `tests/Sms.Tests.Integration/Phase5/StudentParentTests.cs`) to see how this repo builds a
> `WebApplicationFactory<Program>`, mints a token via `IJwtTokenService.IssueAccess(userId, tenantId,
> roles, false)`, sets `Authorization` + `X-Tenant-Id` headers, and seeds rows (direct SQL inserts under
> the tenant's RLS context). Mirror that exact pattern here; do not invent a new abstraction. Insert
> students with the columns from `StudentRepository.Cols`.

- [ ] **Step 2: Run the test — expect FAIL** (route 404/not mapped).

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~ClassStudentsTests`
Expected: FAIL (no such route).

- [ ] **Step 3: Add the repository method**

In `src/Sms.Modules.Sis/Data/StudentRepository.cs`, add (keyset on `Name,Id`; joins Students to the
class's Grade+Section since there is no ClassId):

```csharp
    /// Students belonging to a class, matched by the class's Grade+Section (no ClassId exists).
    /// Keyset paginated on (Name, Id). Returns up to `limit` rows and a NextCursor when a full page returns.
    public async Task<(IReadOnlyList<StudentResponse> Rows, string? NextCursor)> ListByClassPagedAsync(
        Guid classId, int limit, string? cursor, CancellationToken ct = default)
    {
        string? lastName = null; Guid? lastId = null;
        var decoded = Sms.Shared.Kernel.Http.Cursor.Decode(cursor);
        if (decoded is not null)
        {
            var i = decoded.IndexOf('|');
            if (i > 0 && Guid.TryParse(decoded[(i + 1)..], out var g)) { lastName = decoded[..i]; lastId = g; }
        }

        var rows = await QueryInlineAsync<StudentResponse>(
            $@"SELECT TOP (@limit) {Cols} FROM dbo.Students s
               WHERE EXISTS (SELECT 1 FROM dbo.Classes c
                             WHERE c.Id = @classId AND c.Grade = s.Grade AND c.Section = s.Section)
                 AND (@lastName IS NULL OR s.Name > @lastName
                      OR (s.Name = @lastName AND s.Id > @lastId))
               ORDER BY s.Name, s.Id",
            new { classId, limit, lastName, lastId }, ct);

        string? next = rows.Count == limit
            ? Sms.Shared.Kernel.Http.Cursor.Encode($"{rows[^1].Name}|{rows[^1].Id}")
            : null;
        return (rows, next);
    }
```

(Note: `Cols` is the existing private const in this file; reuse it. It is `s`-prefixable because every
listed column lives on `dbo.Students` — the `SELECT TOP (@limit) {Cols}` selects them unqualified, which
is unambiguous since only `dbo.Students` is in the outer FROM.)

- [ ] **Step 4: Add the endpoint**

In `src/Sms.Modules.Sis/SisModule.cs`, inside `MapSisModule` (the group `g` is
`app.MapGroup("/v1").RequireAuthorization()`), add (and `using Sms.Shared.Kernel.Authz;`,
`using Sms.Shared.Kernel.Http;`):

```csharp
        g.MapGet("/classes/{classId:guid}/students", async (
            Guid classId, StudentRepository repo,
            [FromQuery] int? limit, [FromQuery] string? cursor) =>
        {
            var page = new PageRequest(limit ?? 50, cursor);
            var (rows, next) = await repo.ListByClassPagedAsync(classId, page.SafeLimit, page.Cursor);
            return Results.Ok(new CursorPage<StudentResponse>(rows, next));
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);
```

Add `using Microsoft.AspNetCore.Mvc;` if `[FromQuery]` is not already imported (match the Finance module,
which already uses `[FromQuery(Name = ...)]`).

- [ ] **Step 5: Run the tests — expect PASS**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~ClassStudentsTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Modules.Sis/Data/StudentRepository.cs src/Sms.Modules.Sis/SisModule.cs tests/Sms.Tests.Integration/Sis/ClassStudentsTests.cs
git commit -m "feat(sis): GET /classes/{id}/students paginated, teacher-app gated"
```

---

### Task 2: `PATCH` + `DELETE /v1/exam-papers/{id}`

**Files:**
- Create: `db/Sms.Migrations/M0035_Procs_ExamPaper_Edit.cs`
- Modify: `src/Sms.Modules.Academics/Contracts/ExamContracts.cs`
- Modify: `src/Sms.Modules.Academics/Data/ExamRepository.cs`
- Modify: `src/Sms.Modules.Academics/AcademicsModule.cs` (exam-papers section, ~lines 104-124)
- Test: `tests/Sms.Tests.Integration/Academics/ExamPaperEditTests.cs`

**Interfaces:**
- Consumes: `AuthorizationPolicies.TeacherApp`, existing `ExamPaperResponse`, `ExamRepository.PaperCols`.
- Produces: `UpdateExamPaperRequest` record; `ExamRepository.UpdateExamPaperAsync(Guid id,
  UpdateExamPaperRequest r, CancellationToken)` → `ExamPaperResponse?`; `ExamRepository.DeleteExamPaperAsync(
  Guid id, CancellationToken)` → `int` (rows affected).

- [ ] **Step 1: Write the migration**

`db/Sms.Migrations/M0035_Procs_ExamPaper_Edit.cs` (follow the proc-migration style of
`M0018_Procs_Exams.cs` — `Execute.Sql` for `CREATE OR ALTER PROCEDURE`, drop in `Down`). Partial update
via COALESCE so null args leave columns unchanged:

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(35, "Exam paper edit: ExamPaper_Update (partial) + ExamPaper_Delete procs")]
public sealed class M0035_Procs_ExamPaper_Edit : Migration
{
    public override void Up()
    {
        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Update
    @Id uniqueidentifier, @Name nvarchar(120) = NULL, @Subject nvarchar(80) = NULL,
    @SubjectId uniqueidentifier = NULL, @Date date = NULL, @StartTime nvarchar(10) = NULL,
    @DurationMin int = NULL, @MaxMarks int = NULL, @Room nvarchar(40) = NULL,
    @Invigilator1 nvarchar(120) = NULL, @Invigilator2 nvarchar(120) = NULL, @Status nvarchar(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ExamPapers SET
        Name = COALESCE(@Name, Name), Subject = COALESCE(@Subject, Subject),
        SubjectId = COALESCE(@SubjectId, SubjectId), [Date] = COALESCE(@Date, [Date]),
        StartTime = COALESCE(@StartTime, StartTime), DurationMin = COALESCE(@DurationMin, DurationMin),
        MaxMarks = COALESCE(@MaxMarks, MaxMarks), Room = COALESCE(@Room, Room),
        Invigilator1 = COALESCE(@Invigilator1, Invigilator1),
        Invigilator2 = COALESCE(@Invigilator2, Invigilator2), Status = COALESCE(@Status, Status)
    WHERE Id = @Id;

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status
    FROM dbo.ExamPapers WHERE Id = @Id;
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Delete @Id uniqueidentifier AS
BEGIN SET NOCOUNT ON; DELETE FROM dbo.ExamPapers WHERE Id = @Id; END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamPaper_Update;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamPaper_Delete;");
    }
}
```

- [ ] **Step 2: Add the contract**

In `src/Sms.Modules.Academics/Contracts/ExamContracts.cs`, add:

```csharp
public sealed record UpdateExamPaperRequest(
    string? Name, string? Subject, Guid? SubjectId, DateTime? Date, string? StartTime, int? DurationMin,
    int? MaxMarks, string? Room, string? Invigilator1, string? Invigilator2, string? Status);
```

- [ ] **Step 3: Add the repository methods**

In `src/Sms.Modules.Academics/Data/ExamRepository.cs`:

```csharp
    public Task<ExamPaperResponse?> UpdateExamPaperAsync(Guid id, UpdateExamPaperRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ExamPaperResponse>("dbo.ExamPaper_Update", new
        {
            Id = id, r.Name, r.Subject, r.SubjectId, r.Date, r.StartTime, r.DurationMin,
            r.MaxMarks, r.Room, r.Invigilator1, r.Invigilator2, r.Status
        }, ct);

    public Task<int> DeleteExamPaperAsync(Guid id, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.ExamPaper_Delete", new { Id = id }, ct);
```

- [ ] **Step 4: Write the failing test**

`tests/Sms.Tests.Integration/Academics/ExamPaperEditTests.cs`: seed a tenant (teacher token), POST an
exam paper, then PATCH it (assert the changed field round-trips + an untouched field is preserved), then
DELETE it (assert 204 and a subsequent GET returns 404). Also assert a `student.parent` token gets 403 on
PATCH. Mirror the existing Academics integration test setup. (Write real assertions — no stubs.)

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~ExamPaperEditTests`
Expected: FAIL (routes not mapped).

- [ ] **Step 5: Add the endpoints**

In `src/Sms.Modules.Academics/AcademicsModule.cs`, after the existing exam-papers routes (add
`using Sms.Shared.Kernel.Authz;`):

```csharp
        g.MapPatch("/exam-papers/{id:guid}", async (Guid id, UpdateExamPaperRequest req, ExamRepository repo) =>
        {
            var updated = await repo.UpdateExamPaperAsync(id, req);
            return updated is null ? NotFound() : Results.Ok(new DataEnvelope<ExamPaperResponse>(updated));
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapDelete("/exam-papers/{id:guid}", async (Guid id, ExamRepository repo) =>
        {
            await repo.DeleteExamPaperAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);
```

(`NotFound()` is the existing private helper in this file.)

- [ ] **Step 6: Run the tests — expect PASS**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~ExamPaperEditTests`
Expected: PASS. (The dev `MigrationRunner` applies M0035 on startup; the test factory runs migrations too.)

- [ ] **Step 7: Commit**

```bash
git add db/Sms.Migrations/M0035_Procs_ExamPaper_Edit.cs src/Sms.Modules.Academics/Contracts/ExamContracts.cs src/Sms.Modules.Academics/Data/ExamRepository.cs src/Sms.Modules.Academics/AcademicsModule.cs tests/Sms.Tests.Integration/Academics/ExamPaperEditTests.cs
git commit -m "feat(academics): PATCH + DELETE /exam-papers/{id} (M0035 procs), teacher-app gated"
```

---

### Task 3: `GET /v1/me/attendance/history` + `/summary`

**Files:**
- Modify: `src/Sms.Modules.Attendance/AttendanceModule.cs`
- Test: `tests/Sms.Tests.Integration/Attendance/CheckinHistoryTests.cs`

**Interfaces:**
- Consumes: existing `CheckInRepository`, `TeacherAttendanceDayResponse`, private `CheckInRow`/`ToEvent`.
- Produces: `CheckInRepository.GetHistoryAsync(Guid userId, int limit, CancellationToken)` →
  `IReadOnlyList<TeacherAttendanceDayResponse>`; `CheckInRepository.GetSummaryAsync(Guid userId, int year,
  int month, CancellationToken)` → `TeacherAttendanceSummaryResponse`; new record
  `TeacherAttendanceSummaryResponse(int DaysPresent, int DaysFlagged, double TotalHours)`.
- These routes stay on the group's bare `.RequireAuthorization()` (self-scoped by `UserId`; the check-in
  feature serves teachers AND staff, so a role policy would wrongly exclude staff).

- [ ] **Step 1: Write the failing test**

`tests/Sms.Tests.Integration/Attendance/CheckinHistoryTests.cs`: mint a token for a user, insert
`dbo.CheckIns` rows across two days (an `in`+`out` pair on day A both verified; an `in` on day B with
`Verified=0`). Assert:
- `GET /v1/me/attendance/history?limit=30` returns 2 day-objects newest-first, day A having both
  `check_in` and `check_out`.
- `GET /v1/me/attendance/summary?month=YYYY-MM` (the seeded month) returns `days_present = 2`,
  `days_flagged = 1`, and `total_hours` ≈ the A-day in→out span in hours.

Mirror `GeofenceCheckinTests` for setup. Real assertions.

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~CheckinHistoryTests`
Expected: FAIL.

- [ ] **Step 2: Add the summary DTO + repo methods**

In `src/Sms.Modules.Attendance/AttendanceModule.cs` add the record near the other DTOs:

```csharp
public sealed record TeacherAttendanceSummaryResponse(int DaysPresent, int DaysFlagged, double TotalHours);
```

In `CheckInRepository` add:

```csharp
    public async Task<IReadOnlyList<TeacherAttendanceDayResponse>> GetHistoryAsync(
        Guid userId, int limit, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<CheckInRow>(
            "SELECT Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified FROM dbo.CheckIns " +
            "WHERE UserId = @userId ORDER BY At DESC", new { userId }, ct);

        return rows.GroupBy(r => r.At.Date)
            .OrderByDescending(g => g.Key)
            .Take(limit)
            .Select(g => new TeacherAttendanceDayResponse(
                g.Key,
                g.Where(x => x.Kind == "in").OrderBy(x => x.At).Select(ToEvent).LastOrDefault(),
                g.Where(x => x.Kind == "out").OrderBy(x => x.At).Select(ToEvent).LastOrDefault()))
            .ToList();
    }

    public async Task<TeacherAttendanceSummaryResponse> GetSummaryAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<CheckInRow>(
            "SELECT Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified FROM dbo.CheckIns " +
            "WHERE UserId = @userId AND YEAR(At) = @year AND MONTH(At) = @month", new { userId, year, month }, ct);

        var byDay = rows.GroupBy(r => r.At.Date).ToList();
        int daysPresent = byDay.Count(g => g.Any(x => x.Kind == "in"));
        int daysFlagged = byDay.Count(g => g.Any(x => !x.Verified));
        double totalHours = byDay.Sum(g =>
        {
            var firstIn = g.Where(x => x.Kind == "in").OrderBy(x => x.At).Select(x => (DateTime?)x.At).FirstOrDefault();
            var lastOut = g.Where(x => x.Kind == "out").OrderBy(x => x.At).Select(x => (DateTime?)x.At).LastOrDefault();
            return firstIn is { } i && lastOut is { } o && o > i ? (o - i).TotalHours : 0;
        });
        return new TeacherAttendanceSummaryResponse(daysPresent, daysFlagged, Math.Round(totalHours, 2));
    }
```

- [ ] **Step 3: Add the endpoints**

In `MapAttendanceModule` (group `g` = `/v1/me/attendance`), add:

```csharp
        g.MapGet("/history", async (CheckInRepository repo, ITenantContext tenant, [FromQuery] int? limit) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            return Results.Ok(new DataEnvelope<IReadOnlyList<TeacherAttendanceDayResponse>>(
                await repo.GetHistoryAsync(uid, limit is > 0 and <= 366 ? limit.Value : 30)));
        });

        g.MapGet("/summary", async (CheckInRepository repo, ITenantContext tenant, IClock clock, [FromQuery] string? month) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            var now = clock.UtcNow;
            int year = now.Year, m = now.Month;
            if (month is not null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
                    return Results.Json(ErrorEnvelope.From(new Error("invalid_month", "month must be YYYY-MM")), statusCode: 422);
                year = int.Parse(month[..4]); m = int.Parse(month[5..]);
            }
            return Results.Ok(new DataEnvelope<TeacherAttendanceSummaryResponse>(
                await repo.GetSummaryAsync(uid, year, m)));
        });
```

Add `using Microsoft.AspNetCore.Mvc;`, `using Sms.Shared.Kernel.Time;`, and `using Sms.Shared.Kernel.Results;`
if not present (the file already uses `Error`/`ErrorEnvelope` via `Forbidden`).

- [ ] **Step 4: Run the tests — expect PASS**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~CheckinHistoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Modules.Attendance/AttendanceModule.cs tests/Sms.Tests.Integration/Attendance/CheckinHistoryTests.cs
git commit -m "feat(attendance): GET /me/attendance/history + /summary (self-scoped)"
```

---

### Task 4: Revive Reporting module + `GET /v1/dashboard/stats`

**Files:**
- Create: `src/Sms.Modules.Reporting/Contracts/ReportingContracts.cs`
- Create: `src/Sms.Modules.Reporting/Data/ReportingRepository.cs`
- Create: `src/Sms.Modules.Reporting/ReportingModule.cs`
- Modify: `src/Sms.Api/Program.cs` (add `AddReportingModule()` near the other `Add*Module()`; add
  `MapReportingModule(app)` near the other `Map*Module(app)`; add `using Sms.Modules.Reporting;`)
- Test: `tests/Sms.Tests.Integration/Reporting/DashboardStatsTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`/`BaseRepository`, `DataEnvelope<T>`, `AuthorizationPolicies.TeacherApp`,
  `IClock`.
- Produces: `IServiceCollection AddReportingModule(this IServiceCollection)`;
  `IEndpointRouteBuilder MapReportingModule(this IEndpointRouteBuilder)`;
  `ReportingRepository.GetDashboardStatsAsync(DateTime today, CancellationToken)` → `DashboardStatsResponse`;
  record `DashboardStatsResponse(int TotalStudents, int TotalClasses, int AttendanceToday, int PendingAssignments, int UpcomingExams)`.

- [ ] **Step 1: Write the contracts**

`src/Sms.Modules.Reporting/Contracts/ReportingContracts.cs`:

```csharp
namespace Sms.Modules.Reporting.Contracts;

public sealed record DashboardStatsResponse(
    int TotalStudents, int TotalClasses, int AttendanceToday, int PendingAssignments, int UpcomingExams);
```

- [ ] **Step 2: Write the repository**

`src/Sms.Modules.Reporting/Data/ReportingRepository.cs` (RLS scopes every table by tenant, so no explicit
TenantId filter — matches existing repos):

```csharp
using Sms.Modules.Reporting.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Reporting.Data;

public sealed class ReportingRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<DashboardStatsResponse> GetDashboardStatsAsync(DateTime today, CancellationToken ct = default)
    {
        var row = await QueryInlineAsync<DashboardStatsResponse>(@"
SELECT
  (SELECT COUNT(*) FROM dbo.Students)                                            AS TotalStudents,
  (SELECT COUNT(*) FROM dbo.Classes)                                             AS TotalClasses,
  (SELECT COUNT(*) FROM dbo.AttendanceRecords
     WHERE [Date] = @today AND Status IN ('present','late'))                     AS AttendanceToday,
  (SELECT COUNT(*) FROM dbo.Homework
     WHERE Status = 'todo' AND (DueDate IS NULL OR DueDate >= @today))           AS PendingAssignments,
  (SELECT COUNT(*) FROM dbo.ExamPapers
     WHERE Status = 'upcoming' AND ([Date] IS NULL OR [Date] >= @today))         AS UpcomingExams",
            new { today = today.Date }, ct);
        return row[0];
    }
}
```

- [ ] **Step 3: Write the module wiring**

`src/Sms.Modules.Reporting/ReportingModule.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Reporting.Contracts;
using Sms.Modules.Reporting.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Time;

namespace Sms.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<ReportingRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportingModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapGet("/dashboard/stats", async (ReportingRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<DashboardStatsResponse>(
                await repo.GetDashboardStatsAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        return app;
    }
}
```

- [ ] **Step 4: Wire into Program.cs**

Add `using Sms.Modules.Reporting;` with the other module usings; add `builder.Services.AddReportingModule();`
beside the other `Add*Module()` calls; add `app.MapReportingModule();` beside the other `Map*Module(app)`
calls (after auth/tenant middleware, with the other `app.MapXModule()` lines).

- [ ] **Step 5: Write + run the test (FAIL→PASS)**

`tests/Sms.Tests.Integration/Reporting/DashboardStatsTests.cs`: seed a tenant (teacher token) with N
students, M classes, K today-present roll-call rows (mix present/late/absent — assert only present+late
count), a `todo` homework, an `upcoming` exam paper. Assert each stat. Also assert `student.parent` → 403.

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~DashboardStatsTests`
Expected: FAIL first (route missing), then PASS after Steps 1-4.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Modules.Reporting/ src/Sms.Api/Program.cs tests/Sms.Tests.Integration/Reporting/DashboardStatsTests.cs
git commit -m "feat(reporting): revive module + GET /dashboard/stats, teacher-app gated"
```

---

### Task 5: `GET /v1/principal/overview`

**Files:**
- Modify: `src/Sms.Modules.Reporting/Contracts/ReportingContracts.cs`
- Modify: `src/Sms.Modules.Reporting/Data/ReportingRepository.cs`
- Modify: `src/Sms.Modules.Reporting/ReportingModule.cs`
- Test: `tests/Sms.Tests.Integration/Reporting/PrincipalOverviewTests.cs`

**Interfaces:**
- Consumes: same infra as Task 4; `Policies.Principal`.
- Produces: records `PrincipalKpis(decimal StudentsPresentPct, int StaffPresent, int StaffTotal,
  int PendingApprovals)`, `PrincipalStaffEntry(Guid TeacherId, string Name, string Initials, string? Subject,
  string? Phone, bool CheckedIn, DateTime? CheckInAt, string? Role)`,
  `PrincipalOverviewResponse(PrincipalKpis Kpis, IReadOnlyList<PrincipalStaffEntry> Staff)`;
  `ReportingRepository.GetPrincipalOverviewAsync(DateTime today, CancellationToken)`.

- [ ] **Step 1: Add the contracts**

```csharp
public sealed record PrincipalKpis(
    decimal StudentsPresentPct, int StaffPresent, int StaffTotal, int PendingApprovals);

public sealed record PrincipalStaffEntry(
    Guid TeacherId, string Name, string Initials, string? Subject, string? Phone,
    bool CheckedIn, DateTime? CheckInAt, string? Role);

public sealed record PrincipalOverviewResponse(PrincipalKpis Kpis, IReadOnlyList<PrincipalStaffEntry> Staff);
```

- [ ] **Step 2: Add the repository method**

The staff roster left-joins today's verified `in` check-in via the email bridge
(`Teachers.Email = Users.Email` → `Users.Id = CheckIns.UserId`). A private row type captures the raw
staff columns; `Initials`/`Subject` are derived in C#.

```csharp
    private sealed record StaffRow(
        Guid TeacherId, string Name, string? SubjectsCsv, string? Phone, string? Designation,
        DateTime? CheckInAt);

    public async Task<PrincipalOverviewResponse> GetPrincipalOverviewAsync(DateTime today, CancellationToken ct = default)
    {
        var d = today.Date;

        var kpiRows = await QueryInlineAsync<PrincipalKpis>(@"
SELECT
  CAST(CASE WHEN (SELECT SUM(StudentCount) FROM dbo.Classes) > 0
       THEN 100.0 * (SELECT COUNT(*) FROM dbo.AttendanceRecords
                     WHERE [Date] = @d AND Status IN ('present','late'))
              / (SELECT SUM(StudentCount) FROM dbo.Classes)
       ELSE 0 END AS decimal(5,1))                                              AS StudentsPresentPct,
  (SELECT COUNT(DISTINCT t.Id) FROM dbo.Teachers t
     JOIN dbo.Users u ON u.Email = t.Email
     JOIN dbo.CheckIns ci ON ci.UserId = u.Id
     WHERE ci.Kind = 'in' AND ci.Verified = 1 AND CAST(ci.At AS date) = @d)     AS StaffPresent,
  (SELECT COUNT(*) FROM dbo.Teachers WHERE Status = 'active')                   AS StaffTotal,
  (SELECT COUNT(*) FROM dbo.LeaveRequests WHERE Status = 'pending')             AS PendingApprovals",
            new { d }, ct);

        var staffRows = await QueryInlineAsync<StaffRow>(@"
SELECT t.Id AS TeacherId, t.Name, t.SubjectsCsv, t.Phone, t.Designation,
       (SELECT MAX(ci.At) FROM dbo.CheckIns ci
          JOIN dbo.Users u ON u.Id = ci.UserId
          WHERE u.Email = t.Email AND ci.Kind = 'in' AND ci.Verified = 1
            AND CAST(ci.At AS date) = @d) AS CheckInAt
FROM dbo.Teachers t
WHERE t.Status = 'active'
ORDER BY t.Name", new { d }, ct);

        var staff = staffRows.Select(r => new PrincipalStaffEntry(
            r.TeacherId, r.Name, Initials(r.Name),
            string.IsNullOrEmpty(r.SubjectsCsv) ? null : r.SubjectsCsv.Split(',')[0],
            r.Phone, r.CheckInAt is not null, r.CheckInAt,
            string.IsNullOrEmpty(r.Designation) ? "teacher" : r.Designation)).ToList();

        return new PrincipalOverviewResponse(kpiRows[0], staff);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
```

- [ ] **Step 3: Add the endpoint**

In `MapReportingModule`:

```csharp
        g.MapGet("/principal/overview", async (ReportingRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<PrincipalOverviewResponse>(
                await repo.GetPrincipalOverviewAsync(clock.UtcNow))))
            .RequireAuthorization(Policies.Principal);
```

(Add `using Sms.Shared.Kernel.Authz;` — already present for `AuthorizationPolicies`; `Policies` is in the
same namespace.)

- [ ] **Step 4: Write + run the test (FAIL→PASS)**

`tests/Sms.Tests.Integration/Reporting/PrincipalOverviewTests.cs`: seed a tenant (principal token) with
classes (known `StudentCount`), today's roll-call (present+late counted), one active teacher whose `Email`
matches a `Users` row that has a verified `in` check-in today, one active teacher with no check-in, and one
`pending` leave. Assert `staff_total`, `staff_present = 1`, `pending_approvals = 1`,
`students_present_pct`, and that the checked-in teacher shows `checked_in = true` with a `check_in_at`,
the other `false`/null. Assert `school.teacher` → 403.

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~PrincipalOverviewTests`
Expected: PASS after implementation.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Modules.Reporting/ tests/Sms.Tests.Integration/Reporting/PrincipalOverviewTests.cs
git commit -m "feat(reporting): GET /principal/overview (KPIs + staff check-in), principal gated"
```

---

### Task 6: `GET /v1/principal/attendance`

**Files:**
- Modify: `src/Sms.Modules.Reporting/Contracts/ReportingContracts.cs`
- Modify: `src/Sms.Modules.Reporting/Data/ReportingRepository.cs`
- Modify: `src/Sms.Modules.Reporting/ReportingModule.cs`
- Test: `tests/Sms.Tests.Integration/Reporting/PrincipalAttendanceTests.cs`

**Interfaces:**
- Consumes: Task 5's `PrincipalStaffEntry` + `Initials` + the email-bridge staff query (reuse, do not
  duplicate — extract a private `LoadStaffAsync(DateTime d, CancellationToken)` helper in Task 5's repo and
  call it from both overview and attendance).
- Produces: records `PrincipalClassAttendance(Guid ClassId, string ClassName, int Present, int Total,
  decimal Pct)`, `PrincipalAttendanceResponse(DateTime Date, int PresentTotal, int StudentTotal,
  decimal OverallPct, IReadOnlyList<PrincipalClassAttendance> Classes, IReadOnlyList<PrincipalStaffEntry> Staff)`;
  `ReportingRepository.GetPrincipalAttendanceAsync(DateTime today, CancellationToken)`.

- [ ] **Step 1: Refactor staff loading to a shared private method**

In `ReportingRepository`, extract the staff-roster query from Task 5 into
`private async Task<IReadOnlyList<PrincipalStaffEntry>> LoadStaffAsync(DateTime d, CancellationToken ct)`
and have `GetPrincipalOverviewAsync` call it (DRY — the staff[] block is identical in both endpoints). Run
the Task 5 test to confirm no regression:

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~PrincipalOverviewTests`
Expected: PASS (unchanged behavior).

- [ ] **Step 2: Add the contracts**

```csharp
public sealed record PrincipalClassAttendance(
    Guid ClassId, string ClassName, int Present, int Total, decimal Pct);

public sealed record PrincipalAttendanceResponse(
    DateTime Date, int PresentTotal, int StudentTotal, decimal OverallPct,
    IReadOnlyList<PrincipalClassAttendance> Classes, IReadOnlyList<PrincipalStaffEntry> Staff);
```

- [ ] **Step 3: Add the repository method**

```csharp
    public async Task<PrincipalAttendanceResponse> GetPrincipalAttendanceAsync(DateTime today, CancellationToken ct = default)
    {
        var d = today.Date;
        var classes = await QueryInlineAsync<PrincipalClassAttendance>(@"
SELECT c.Id AS ClassId, c.Name AS ClassName,
       ISNULL(a.Present, 0) AS Present, c.StudentCount AS Total,
       CAST(CASE WHEN c.StudentCount > 0 THEN 100.0 * ISNULL(a.Present,0) / c.StudentCount ELSE 0 END AS decimal(5,1)) AS Pct
FROM dbo.Classes c
OUTER APPLY (SELECT COUNT(*) AS Present FROM dbo.AttendanceRecords ar
             WHERE ar.ClassId = c.Id AND ar.[Date] = @d AND ar.Status IN ('present','late')) a
ORDER BY c.Name", new { d }, ct);

        int presentTotal = classes.Sum(c => c.Present);
        int studentTotal = classes.Sum(c => c.Total);
        decimal overall = studentTotal > 0 ? Math.Round(100m * presentTotal / studentTotal, 1) : 0m;
        var staff = await LoadStaffAsync(d, ct);
        return new PrincipalAttendanceResponse(d, presentTotal, studentTotal, overall, classes, staff);
    }
```

- [ ] **Step 4: Add the endpoint**

```csharp
        g.MapGet("/principal/attendance", async (ReportingRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<PrincipalAttendanceResponse>(
                await repo.GetPrincipalAttendanceAsync(clock.UtcNow))))
            .RequireAuthorization(Policies.Principal);
```

- [ ] **Step 5: Write + run the test (FAIL→PASS)**

`tests/Sms.Tests.Integration/Reporting/PrincipalAttendanceTests.cs`: seed two classes (known
`StudentCount`); mark today's roll-call for one (mix present/late/absent), leave the other un-marked.
Assert `present_total`/`student_total`/`overall_pct`, the per-class breakdown (un-marked class at
`present = 0`, `pct = 0`), and the `staff[]` block present. Assert `school.teacher` → 403.

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~PrincipalAttendanceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Modules.Reporting/ tests/Sms.Tests.Integration/Reporting/PrincipalAttendanceTests.cs
git commit -m "feat(reporting): GET /principal/attendance (school-wide + per-class), principal gated"
```

---

### Task 7: Swagger audience mapping

**Files:**
- Modify: `src/Sms.Api/Swagger/ApiAudienceMap.cs`
- Test: `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: the Teacher Swagger doc now lists the principal + new screen routes.

- [ ] **Step 1: Write the failing assertions**

In `tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs`, add assertions that the `teacher` doc's
paths contain `/v1/approvals`, `/v1/principal/overview`, `/v1/principal/attendance`,
`/v1/classes/{classId}/students`, and `/v1/dashboard/stats`. (Match the existing `Doc(app, "teacher")`
helper used at line 67.)

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~SwaggerPerAppTests`
Expected: FAIL (these paths not yet in the teacher doc).

- [ ] **Step 2: Update the audience map**

In `src/Sms.Api/Swagger/ApiAudienceMap.cs` `Rules` (most-specific-first ordering preserved):
- Change `("v1/approvals", [SchoolAdmin])` → `("v1/approvals", [SchoolAdmin, Teacher])`.
- Add `("v1/principal", [Teacher])` (place among the school-scoped rules).
- Add `("v1/dashboard/stats", [Teacher])` **before** any broader `v1/dashboard` rule. Note: the existing
  `("v1/dashboard", [CatreAdmin])` rule matches `v1/dashboard/overview`; because matching is most-specific
  segment-prefix first, add `v1/dashboard/stats` as its own earlier entry so it maps to Teacher without
  disturbing the Catre `dashboard/overview` mapping.
- `v1/classes/{classId}/students` is already covered by the existing `("v1/classes", [SchoolAdmin, Teacher])`
  rule — no new entry needed (confirm in the test that it appears in the teacher doc).

- [ ] **Step 3: Run the tests — expect PASS**

Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~SwaggerPerAppTests`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Sms.Api/Swagger/ApiAudienceMap.cs tests/Sms.Tests.Integration/Swagger/SwaggerPerAppTests.cs
git commit -m "feat(api): expose principal + new teacher routes in the Teacher Swagger doc"
```

---

## Self-Review

- **Spec coverage:** §5.1 `classes/{id}/students` (Task 1, Grade+Section per amendment), exam-papers
  PATCH/DELETE (Task 2), check-in history/summary (Task 3); §5.2 dashboards (Tasks 4-6); §6 Swagger (Task 7).
  Assignments correctly **excluded** (moved to Phase 3 per the amendment). §8 pagination applied to the one
  large list this phase adds (Task 1). §9 validation: `invalid_month` 422 (Task 3), 404 on missing exam
  paper (Task 2); other new reads take no user input. §7 authz: every new endpoint carries its policy
  (`TeacherApp` for teacher screens, `Policies.Principal` for principal, bare self-auth for `me/attendance`).
- **Placeholder scan:** none — SQL, repo methods, endpoint code, migration, and the key tests are spelled
  out; the few "mirror the existing test setup" notes point at named existing files because the fixture
  pattern is repo-specific and must be copied, not invented.
- **Type consistency:** `ListByClassPagedAsync`→`CursorPage<StudentResponse>`; `UpdateExamPaperRequest`/
  `UpdateExamPaperAsync`/`DeleteExamPaperAsync`; `GetHistoryAsync`/`GetSummaryAsync`/
  `TeacherAttendanceSummaryResponse`; `ReportingRepository.GetDashboardStatsAsync`/`GetPrincipalOverviewAsync`/
  `GetPrincipalAttendanceAsync` + `LoadStaffAsync` shared helper (Task 6 Step 1) + `Initials`; DTO names
  (`PrincipalKpis`/`PrincipalStaffEntry`/`PrincipalOverviewResponse`/`PrincipalClassAttendance`/
  `PrincipalAttendanceResponse`) are used consistently across Tasks 5-6.

## Next phases
- **Phase 3** — new-data features: timetable, calendar, library, **and assignments** (new `Assignments`
  table); migrations M0036+, RLS, dev seeds.
- **Phase 4** — bus-duty teacher view.
- **Phase 5** — final Swagger/test sweep + full integration run.
