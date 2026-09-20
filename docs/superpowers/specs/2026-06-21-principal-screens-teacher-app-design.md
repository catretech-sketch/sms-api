# Principal screens in the Teacher app — Design

> **⚠️ Superseded by** [`2026-06-21-teacher-principal-app-complete-design.md`](./2026-06-21-teacher-principal-app-complete-design.md),
> which absorbs this spec's principal `overview`/`attendance` + Swagger mapping into a larger
> whole-app effort. Kept for history.
> **Status:** Design (approved scope: map existing + build the two missing endpoints).
> **Context:** The `sms-teacher-app` serves **both `teacher` and `principal`** roles (principal is a
> superset — see [`docs/api/teacher-api.md`](../../api/teacher-api.md) §4). The principal-only screens
> need their APIs exposed in the Teacher App Swagger document. Two of those endpoints don't exist yet.

## 1. Problem

The Teacher App API (Swagger doc `teacher`) is missing the principal screens' endpoints:

| Endpoint | Today | Target |
|---|---|---|
| `POST /v1/announcements` (broadcast) | ✅ already in Teacher doc | unchanged |
| `GET /v1/approvals` | exists, mapped **School-Admin only** | also in Teacher doc |
| `PATCH /v1/approvals/{id}` | exists, mapped **School-Admin only** | also in Teacher doc |
| `GET /v1/principal/overview` | **not implemented** | build + map to Teacher |
| `GET /v1/principal/attendance` | **not implemented** | build + map to Teacher |

So there are two halves: **(a)** expose endpoints that already exist to the Teacher Swagger doc, and
**(b)** build the two missing principal-dashboard aggregation endpoints.

## 2. Swagger audience mapping (half a)

In `src/Sms.Api/Swagger/ApiAudienceMap.cs` `Rules`:

- `v1/approvals` → `[SchoolAdmin, Teacher]` (was `[SchoolAdmin]`).
- add `v1/principal` → `[Teacher]` (place with the other school-scoped rules; order doesn't collide
  with any existing prefix).
- `v1/announcements` already lists `Teacher` — no change.

This is the entire change needed for the endpoints to appear in the **Teacher App API** Swagger doc;
no module files need touching for the mapping.

## 3. Home for the new endpoints (half b)

**The endpoints live in `Sms.Modules.Reporting`** — currently an empty placeholder module. Reporting /
cross-module dashboard aggregation is exactly its purpose, and it keeps attendance/class/staff
aggregation out of the individual domain modules. The two endpoints read across several modules'
tables via Dapper (the established repository pattern), so a single `PrincipalRepository` there is the
natural fit.

> **Open question flagged for review:** the module-placement answer came back ambiguous ("all"). This
> design assumes **revive `Sms.Modules.Reporting`**. If Staffing was intended instead, the only delta is
> file location + skipping the new-module wiring in §6; the queries and DTOs are unchanged.

New files:

- `src/Sms.Modules.Reporting/ReportingModule.cs` — DI registration (`AddReportingModule`) + endpoint
  mapping (`MapReportingModule`).
- `src/Sms.Modules.Reporting/Contracts/PrincipalContracts.cs` — response DTOs.
- `src/Sms.Modules.Reporting/Data/PrincipalRepository.cs` — the aggregation queries.

Wiring in `src/Sms.Api/Program.cs`: `builder.Services.AddReportingModule();` and
`app.MapReportingModule();` (alongside the other `Add*Module` / `Map*Module` calls). The API already
references every module, so the Reporting project must reference `Sms.Shared.Kernel` only (it queries
`dbo.*` tables directly — it does **not** take a code dependency on Academics/Staffing).

## 4. Endpoints & wire contracts

Both are `GET`, tenant-scoped (`.RequireAuthorization()`), snake_case, wrapped in the standard
`DataEnvelope<T>` (`{ "data": … }`).

### `GET /v1/principal/overview`
```json
{
  "kpis": {
    "students_present_pct": 0,
    "staff_present": 0,
    "staff_total": 0,
    "pending_approvals": 0
  },
  "staff": [
    { "teacher_id": "...", "name": "...", "initials": "..", "subject": "...",
      "phone": "...", "checked_in": false, "check_in_at": null, "role": "..." }
  ]
}
```

### `GET /v1/principal/attendance`
```json
{
  "date": "2026-06-21",
  "present_total": 0,
  "student_total": 0,
  "overall_pct": 0,
  "classes": [
    { "class_id": "...", "class_name": "...", "present": 0, "total": 0, "pct": 0 }
  ],
  "staff": [ /* same staff[] shape as overview */ ]
}
```

DTO records (PascalCase → snake_case via the global naming policy):

- `PrincipalOverviewResponse(PrincipalKpis Kpis, IReadOnlyList<PrincipalStaffEntry> Staff)`
- `PrincipalKpis(decimal StudentsPresentPct, int StaffPresent, int StaffTotal, int PendingApprovals)`
- `PrincipalStaffEntry(Guid TeacherId, string Name, string Initials, string? Subject, string? Phone, bool CheckedIn, DateTime? CheckInAt, string? Role)`
- `PrincipalAttendanceResponse(DateTime Date, int PresentTotal, int StudentTotal, decimal OverallPct, IReadOnlyList<PrincipalClassAttendance> Classes, IReadOnlyList<PrincipalStaffEntry> Staff)`
- `PrincipalClassAttendance(Guid ClassId, string ClassName, int Present, int Total, decimal Pct)`

## 5. Aggregation rules (decisions)

- **"Present" = roll-call status `present` OR `late`.** (Confirmed: late counts as in-school.)
  `holiday`/`absent`/`leave` do not count as present.
- **`student_total`** = `SUM(Classes.StudentCount)` for the tenant. **`present_total`** = count of
  today's `dbo.AttendanceRecords` rows with status in (`present`,`late`). Per class: `total` =
  that class's `StudentCount`, `present` = its present-counted records for today; a class with no
  roll-call yet → `present = 0`, `pct = 0`.
- **`students_present_pct` / `overall_pct`** = `present_total * 100 / NULLIF(student_total,0)`, rounded
  to one decimal; `0` when `student_total = 0`.
- **`staff_total`** = count of `dbo.Teachers` with `Status` active (consistent with the teacher-list
  endpoint's notion of active). **`staff_present`** = distinct teachers with a **verified** `in`
  check-in dated today.
- **Staff check-in link (the indirect bridge):** `Teachers` has no `UserId`. Join
  `Teachers.Email = Users.Email` (same tenant) → `Users.Id = CheckIns.UserId`, filter `Kind='in'`,
  `Verified=1`, `CAST(At AS date)=today`. A teacher with no matching user, or no check-in, shows
  `checked_in: false`, `check_in_at: null`. `check_in_at` = the latest qualifying `in` time.
- **`pending_approvals`** = count of `dbo.LeaveRequests` with `Status='pending'` (reuses the same
  source as `GET /v1/approvals`).
- **`initials`** derived from `Name` (first letters of up to two words), matching how other DTOs in
  the contract carry `initials`.
- **`subject`** = first of the teacher's subjects (`SubjectsCsv` first token); `role` = teacher
  `Designation` (falls back to "teacher").
- **"Today"** uses the injected `IClock` (UTC), consistent with the rest of the codebase.

The whole thing is a small number of set-based queries (one for class/student aggregation, one for the
staff roster + check-in left-join, one count for pending approvals) — RLS already scopes every table by
tenant, so no explicit `TenantId` filter is needed beyond what RLS enforces, matching existing repos.

## 6. Authorization

Match the existing convention: `.RequireAuthorization()` only (tenant-scoped, any authenticated user).
Server-side **principal-role** enforcement is **not wired anywhere** in the codebase today — only the
`platform` policy is registered in `Program.cs`; the `Policies.Principal` constant exists but is unused,
and role gating is currently frontend-driven. This design stays consistent and does **not** add new
enforcement.

> **Follow-up (out of scope):** wire real role policies (`Policies.Principal`, etc.) across the
> principal-only + school-admin endpoints. Tracked separately; noted here so it isn't forgotten.

## 7. Testing

Integration tests in `tests/Sms.Tests.Integration` (new `Reporting/PrincipalTests.cs`), following the
existing module-test pattern (tenant seed + RLS context). Seed: a tenant with two classes (known
`StudentCount`), today's roll-call for one class (mix of present/late/absent), one teacher whose
`Email` matches a user with a verified `in` check-in today, one teacher with no check-in, and one
pending leave request. Assert:

- `overview`: `staff_total`, `staff_present` (= 1), `pending_approvals` (= 1), `students_present_pct`
  computed correctly with late counted; `staff[]` shows the right `checked_in`/`check_in_at`.
- `attendance`: `present_total` / `student_total` / `overall_pct`, the per-class breakdown (including
  the un-marked class at `present = 0`), and the `staff[]` block.

## 8. Out of scope

- The rest of the Phase-3 Teacher contract (timetable, assignments, calendar, library, bus, chat
  threads) — not requested here.
- Real server-side role enforcement (see §6 follow-up).
- Cursor paging (§5 of the contract) — these lists are school-staff/class sized, small.
