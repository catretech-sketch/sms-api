# Teacher + Principal mobile app — complete backend, production level — Design

> **Status:** Design (approved scope: build **all** missing/changed endpoints in one spec, to production
> level — role-based authz, pagination, validation/errors, integration tests).
> **Supersedes:** [`2026-06-21-principal-screens-teacher-app-design.md`](./2026-06-21-principal-screens-teacher-app-design.md)
> — that smaller spec's content (principal `overview`/`attendance` + Swagger mapping) is **absorbed** into
> §5.2 and §6 below.
> **Source of truth for "all screens":** [`docs/api/teacher-api.md`](../../api/teacher-api.md), which is
> derived directly from the `sms-teacher-app` domain/repo files — so it is a faithful proxy for the app's
> screens.

---

## 1. Goal

Make the backend serve **every** screen of the `sms-teacher-app` (one app, two roles: `teacher` and
`principal`, principal a superset), at production quality. This means closing ~17 endpoint gaps **and**
adding four cross-cutting production capabilities (authz, pagination, validation, tests).

## 2. Gap table (the work-list)

✅ built · ⚠️ exists, needs change · ❌ missing. Only ⚠️/❌ rows are in scope.

| # | Area | Endpoint | Status | Plan |
|---|---|---|---|---|
| 1 | Classes | `GET /v1/classes/{id}/students` | ❌ | new route, reuse student data |
| 2 | Exam papers | `PATCH /v1/exam-papers/{id}` | ❌ | new, reuse table |
| 3 | Exam papers | `DELETE /v1/exam-papers/{id}` | ❌ | new, reuse table |
| 4 | Assignments | `GET/POST /v1/assignments` | ⚠️ exists as `/homework` | reconcile (§5.4) |
| 5 | Self check-in | `GET /v1/me/attendance/history` | ❌ | new, reuse `CheckIns` |
| 6 | Self check-in | `GET /v1/me/attendance/summary` | ❌ | new, reuse `CheckIns` |
| 7 | Dashboard | `GET /v1/dashboard/stats` | ❌ | new aggregation, reuse data |
| 8 | Principal | `GET /v1/principal/overview` | ❌ | new aggregation (§5.2) |
| 9 | Principal | `GET /v1/principal/attendance` | ❌ | new aggregation (§5.2) |
| 10 | Principal | `GET /v1/approvals`, `PATCH /v1/approvals/{id}` | ⚠️ School-Admin-only | re-map + role (§6) |
| 11 | Timetable | `GET /v1/timetable` | ❌ | **new table** (§5.3) |
| 12 | Calendar | `GET /v1/calendar` | ❌ | **new table** (§5.3) |
| 13 | Library | `GET /v1/library` | ❌ | **new table** (§5.3) |
| 14 | Bus duty | `GET /v1/bus/assigned`, `bus/{id}/position`, `bus/{id}/roster`, `POST /bus/{id}/boarding` | ❌ | **new tables**, reuse Transport (§5.5) |
| 15 | Swagger | map `approvals` + `principal` into Teacher doc | ⚠️ | `ApiAudienceMap` (§6) |

Endpoints already ✅ (auth, classes list, roll-call, exam-papers GET/POST, grades, threads,
announcements GET/POST, payslips, check-in school-location/today/punch, leave) are untouched **except**
where the authz/pagination passes (§7, §8) apply to them.

## 3. Architecture & module placement

Follow the existing module pattern (each module = `Contracts/` + `Data/` + `<Name>Module.cs` with
`Add<Name>Module()` / `Map<Name>Module()`, wired in `Program.cs`). New endpoints land in the module that
owns their domain; cross-cutting aggregations land in the revived **`Sms.Modules.Reporting`** module.

| Endpoint(s) | Module |
|---|---|
| `classes/{id}/students` | `Sms.Modules.Sis` (owns student data) |
| `exam-papers` PATCH/DELETE | `Sms.Modules.Academics` (owns `ExamRepository`) |
| `assignments` | `Sms.Modules.Academics` (homework domain) |
| `me/attendance/history`,`summary` | `Sms.Modules.Attendance` |
| `dashboard/stats`, `principal/overview`, `principal/attendance` | `Sms.Modules.Reporting` (revived) |
| `timetable`, `calendar`, `library` | **new `Sms.Modules.Schedule`** (timetable+calendar) and `Sms.Modules.Academics` (library) — see §5.3 |
| bus duty (teacher view) | `Sms.Modules.Transport` (owns trip/route/boarding data) |

> **Decision:** timetable + calendar share a small new module `Sms.Modules.Schedule` (both are
> date/period scheduling, neither fits an existing module cleanly). Library is academic resource data →
> Academics. If you'd rather not add a module, timetable/calendar can go into Academics too; flagged for
> review.

## 4. Migrations

New tables use FluentMigrator with the established per-table RLS pattern (see `M0021_Geofence_Tables.cs`):
GUID PK `NewSequentialId`, `TenantId` NOT NULL, an `rls.<Table>TenantPolicy` FILTER+BLOCK predicate, and
a covering index. Migrations are numbered from **M0035** upward (current head is M0034) and must pass the
existing `MigrationIdempotenceTests`. Each new table ships a **dev seed** (idempotent, dev-environment
only, consistent with `MigrationRunner.Run` being dev-only) so the screens have data to render.

New tables: `TimetableSlots`, `CalendarEvents`, `LibraryBooks`, `BusAssignments` (+ any bus support
tables in §5.5).

## 5. Feature designs

All endpoints: tenant-scoped (RLS), snake_case wire, `DataEnvelope<T>`/`CursorPage<T>` envelope, error
envelope on failure. DTO records named PascalCase → snake_case by the global naming policy.

### 5.1 Reuse-existing-data endpoints (no new tables)
- **`GET /v1/classes/{classId}/students`** → `Student[]` (existing `StudentResponse`). New
  `StudentRepository.ListByClassAsync(classId, cursor, limit)`. Paginated (§8).
- **`PATCH /v1/exam-papers/{id}`** (partial update) and **`DELETE /v1/exam-papers/{id}`** →
  `ExamRepository.UpdateExamPaperAsync` / `DeleteExamPaperAsync`. `204`.
- **`GET /v1/me/attendance/history?limit=30`** → `TeacherAttendanceDay[]` (existing DTO) — `CheckIns`
  grouped by day, newest first, `limit` honoured. **`GET /v1/me/attendance/summary?month=YYYY-MM`** →
  `{ days_present, days_flagged, total_hours }` aggregated from `CheckIns` for the month.

### 5.2 Principal + teacher dashboards (`Sms.Modules.Reporting`, reused data)
Revive the empty `Sms.Modules.Reporting` module. `PrincipalRepository` aggregates across `dbo.*` tables
via Dapper (depends on `Sms.Shared.Kernel` only).

- **`GET /v1/dashboard/stats`** → `{ total_students, total_classes, attendance_today, pending_assignments, upcoming_exams }`.
- **`GET /v1/principal/overview`** → `{ kpis: { students_present_pct, staff_present, staff_total, pending_approvals }, staff: [ { teacher_id, name, initials, subject, phone, checked_in, check_in_at?, role? } ] }`.
- **`GET /v1/principal/attendance`** → `{ date, present_total, student_total, overall_pct, classes: [ { class_id, class_name, present, total, pct } ], staff: [...] }`.

Aggregation rules (carried from the absorbed spec):
- **"Present" = roll-call `present` OR `late`** (confirmed). `student_total` = `SUM(Classes.StudentCount)`;
  `present_total` = today's `AttendanceRecords` counted present; a class with no roll-call → `present=0`.
- `staff_total` = active `Teachers`; `staff_present` = distinct teachers with a **verified** `in`
  check-in today. **Teacher↔check-in bridge is indirect:** `Teachers` has no `UserId`; join
  `Teachers.Email = Users.Email` (same tenant) → `Users.Id = CheckIns.UserId`. Unlinked teacher →
  `checked_in:false`.
- `pending_approvals` = `LeaveRequests` where `Status='pending'`. "Today" via `IClock` (UTC).
- `pct` = `present*100 / NULLIF(total,0)`, 1 decimal, `0` when total is 0.

### 5.3 New-data features (new tables + migration + seed)
- **`GET /v1/timetable`** → `TimetableSlot[]`: `id·day(Mon..Fri)·period·subject·class_id·class_name·room·start_time·end_time`.
  Table `TimetableSlots`. **Teacher-scoped** where determinable (slots for classes the teacher teaches via
  `Classes.ClassTeacherId` / `Subjects.TeacherId`); falls back to all tenant slots if no teacher link.
- **`GET /v1/calendar`** → `CalendarEvent[]`: `id·title·date·time?·type(exam|holiday|meeting|event|deadline)·description?`.
  Table `CalendarEvents`.
- **`GET /v1/library`** → `LibraryBook[]`: `id·title·author·subject·issued_to?·due_date?·status(available|issued|overdue)`.
  Table `LibraryBooks`. `overdue` derived from `due_date < today` when `issued`.

### 5.4 Assignments ↔ homework reconciliation
The app calls `/v1/assignments`; the backend has `/v1/homework` (different shape). **Decision:** add
canonical **`GET/POST /v1/assignments`** in Academics, backed by the existing **Homework** domain/table,
returning the contract's `Assignment` DTO (`id·title·class_id·class_name·subject·due_date·submissions_count·total_students·status(active|due_soon|overdue|closed)·description?·image_uri?`).
`submissions_count` / `total_students` computed via joins (submissions vs class `StudentCount`); `status`
derived from `due_date` + close state. Keep `/homework` as-is (no break for other consumers); if the
Homework table lacks a needed column (e.g. `image_uri`), the M0035+ migration adds it. The plan confirms
exact columns against the Homework schema.

### 5.5 Bus duty — teacher view (heaviest; new tables, reuse Transport)
The Transport module already owns driver-side trips/pings/boarding. The teacher "assigned bus" view reads
that data from the teacher's perspective plus a new teacher↔bus assignment.
- New `BusAssignments` (TeacherUserId, BusId) and, if not already present, bus/route/stop reference data
  (the plan audits Transport's existing tables first and reuses them; only genuinely missing tables are
  added).
- **`GET /v1/bus/assigned`** → `Bus` (`id·bus_no·route_name·driver·driver_phone·stops[]`).
- **`GET /v1/bus/{busId}/position`** → from the latest Transport trip ping (`current_stop_index·progress·lat·lng·next_stop_name·eta_minutes`).
- **`GET /v1/bus/{busId}/roster`** → `BoardingRecord[]`; **`POST /v1/bus/{busId}/boarding`** bulk upsert → `204`.

> This is the largest single feature. If it threatens the one-pass timeline it is the natural candidate to
> split off — flagged, but in scope per "build all".

## 6. Swagger audience mapping
In `src/Sms.Api/Swagger/ApiAudienceMap.cs`:
- `v1/approvals` → `[SchoolAdmin, Teacher]`; add `v1/principal` → `[Teacher]`; add `v1/timetable`,
  `v1/calendar`, `v1/library`, `v1/assignments`, `v1/bus`, `v1/dashboard/stats` to `[Teacher]`
  (and `[Student]`/`[SchoolAdmin]` where the contract shares them — calendar/library/announcements are
  cross-app). `v1/announcements` already includes Teacher.
- `SwaggerPerAppTests` gets new assertions that the Teacher doc contains the new principal + screen paths.

## 7. Production hardening — Role-based authz
**Today there is no server-side role enforcement** — only the `platform` policy exists; modules use bare
`.RequireAuthorization()`. Role strings are also inconsistent: canonical `Policies` =
`school.admin`/`school.principal`/`school.teacher`/`staff`/`student.parent`, but some tests issue bare
`"teacher"`.

- **Standardize** on the `Policies` constants everywhere. JWT already emits `role` claims and
  `RoleClaimType="role"`, so `RequireRole`/`RequireClaim` policies work.
- **Register** named policies in `Program.cs` for each role and the needed combinations, e.g.
  `school.staff` (any of admin/principal/teacher/staff), `school.principal+` (principal or admin).
- **Apply**: principal-only endpoints (`approvals`, `principal/*`, `announcements` POST) →
  principal-or-admin; teacher shared endpoints → teacher-or-principal-or-admin; school-admin-only
  endpoints keep `school.admin`.
- **Breaking-change note:** integration tests that mint bare `"teacher"` tokens (Comms, Leave/Approvals,
  Geofence) must switch to `school.teacher`/`school.principal`. The plan updates them alongside.

## 8. Production hardening — Pagination
Add a shared `CursorPage<T>` (`{ "data": [...], "next_cursor": "..."|null }`) helper in
`Sms.Shared.Kernel.Http`. Apply **keyset (cursor) paging** to large lists — **students**
(`/v1/students`, `/v1/classes/{id}/students`) and **threads** — with `?limit=` (default 50, max 200) and
`?cursor=`. Cursor is an opaque base64 of the last row's sort key (e.g. `(Name,Id)`). Small lists
(classes, teachers, calendar, library, timetable) stay single-page but return the same envelope shape so
the client can "code defensively for `next_cursor`" per contract §5.

## 9. Production hardening — Validation & errors
Every new write endpoint validates input and returns `422` with the standard
`ErrorEnvelope.From(new Error(code, message))` on failure (required fields, enum membership, date ranges,
`limit` bounds). Reuse the existing `GlobalExceptionHandler` + `ProblemDetails` for unhandled paths. No
new framework — a small inline-guard style consistent with `AuthEndpoints` (`422 invalid_*`).

## 10. Production hardening — Tests
Integration tests in `tests/Sms.Tests.Integration`, one file per feature area, following the existing
tenant-seed + RLS-context pattern. Coverage per new endpoint: happy path + shape, **authz** (403 for the
wrong role, 200 for the right one), **pagination** (cursor round-trip on students/threads), and
validation (422 on bad input). Update the existing bare-`"teacher"` tests to canonical roles (§7).
Unit tests for pure logic (cursor encode/decode, present-pct math, overdue derivation).

## 11. Build order within the single plan
To keep the big plan reviewable, the implementation plan sequences work so each step is independently
verifiable:
1. **Authz + pagination + validation scaffolding** (policies in `Program.cs`, `CursorPage<T>`, role-string
   standardization + test updates) — the foundation every later endpoint uses.
2. **Reuse-data endpoints** (§5.1) + **dashboards** (§5.2) + **Swagger mapping** (§6).
3. **New-data features** (§5.3) + **assignments reconciliation** (§5.4) — migrations M0035+, seeds.
4. **Bus duty** (§5.5) — migrations, Transport reuse.
5. Final **Swagger/test sweep** — `SwaggerPerAppTests` assertions, full integration run.

## 12. Open questions flagged for review
1. **New module `Sms.Modules.Schedule`** for timetable+calendar (vs folding into Academics)? Spec assumes
   the new module (§3).
2. **Bus duty** is the heaviest feature; confirm it stays in this single pass vs. splitting to a follow-up
   (§5.5).
3. **Assignments**: reuse the Homework table (spec's choice) vs. a fresh `Assignments` table (§5.4).

## 13. Out of scope
- Frontend changes; the app already defines these DTOs.
- Real-time push (bus position is poll-based per contract).
- Non-teacher-app surfaces (Catre admin, school admin web, student/parent, staff) except where an
  endpoint is shared and the mapping/authz pass touches it.
