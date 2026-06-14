# Frontend Field Alignment — All Apps to School Admin CRM Canonical Contract

## Context

Five frontend apps under `D:\SMS\sms-project` currently disagree on field names, ID types, enum
values, and entity meaning for the same concepts. `sms-admin` (School Admin CRM) is the agreed
**canonical reference**. The authoritative field list is **§3B "Canonical data dictionary"** in
`2026-06-13-backend-api-design.md` (same folder). This spec defines how to make every other app
speak that exact contract **at the data boundary**, end-to-end, production-grade, without rewriting
UI components.

**Approach (decided):** *canonical mapper boundary* — only each app's DTO interfaces, mapper
functions, and endpoint paths change. UI types and components stay intact and keep working through
the mappers. **Scope (decided):** `sms-teacher-app`, `sms-student`, `sms-staff`, `sms-catreadmin`.
`sms-admin` is the reference and is **not** modified.

## Goal & success criteria

1. Every DTO crossing the wire uses the canonical snake_case School-Admin field names, enums, and
   ID types (GUID strings).
2. Each app's mapper translates canonical DTO ↔ that app's existing UI/domain type — no component,
   screen, or hook is renamed.
3. Each app has a passing **contract test** asserting its DTOs match the canonical keys/enums.
4. All existing app tests stay green (no UI behavior change).

## Current boundary state (verified)

| App | Boundary today | Work type |
|---|---|---|
| `sms-teacher-app` | `src/data/http/mappers.ts` — snake_case DTOs + `to*` mappers exist, but speak the app's own partial contract | **correct** existing DTOs to canonical |
| `sms-staff` | `src/data/http/mappers.ts` — same pattern, mostly canonical already | **correct** the few divergent fields |
| `sms-student` | `src/services/http/index.ts` — all `NotImplementedError` stubs, no DTOs | **build** DTOs + mappers + impls |
| `sms-catreadmin` | none — reads `data.jsx` directly | **introduce** a data-access seam |

## Architecture

Each app keeps the existing seam: `domain/UI types → mapper (DTO↔domain) → http repo (routes) → API`.
Only the middle two layers change. Pattern (already used by teacher/staff):

```
interface XDTO { /* canonical snake_case fields */ }
const toX = (d: XDTO): X => ({ /* map to existing domain type */ });
const fromX = (x: X): Partial<XDTO> => ({ /* for writes */ });
```

Where the canonical contract carries fields the app's domain type doesn't use, the **DTO still
declares them** (so the wire matches the CRM) and the mapper simply ignores the extras. Where the
domain uses a coded value (e.g. attendance `'P'`), the mapper translates canonical↔code.

---

## Per-app DTO deltas

The full target field list per entity is §3B of the backend design doc. Below are the concrete
changes from each app's *current* boundary.

### sms-teacher-app  (`src/data/http/mappers.ts`, `src/data/http/*.repo.ts`, tests)

- **StudentDTO** → canonical `Student`: rename `attendance`→`attendance_pct`,
  `parent`/`parent_phone`→`guardian_name`/`guardian_phone`; **add** `admission_no`, `gender`,
  `grade`, `section`, `class_label`, `fee_status`, `fee_due`, `house`, `avatar_hue`, `status`.
  Keep `roll: string`, `class_id`. `toStudent` maps the subset the domain `Student` uses.
- **ExamDTO → ExamPaperDTO**: rename `title`→`name`, `time`→`start_time`, `duration`→`duration_min`,
  `max_marks` (keep); **add** `exam_id`, `room`, `invigilator1`, `invigilator2`. Status enum becomes
  the canonical union; `toExam` maps back to domain `Exam` (`name`→title, `duration_min`→duration,
  `max_marks`→maxMarks).
- **GradeDTO**: rename `exam_id`→`exam_paper_id`; **add** `id`, `gpa`, `pass`, `date`.
- **AttendanceRecordDTO**: `status` enum → `present|absent|late|leave|holiday`; mapper translates to
  domain `'P'|'A'|'L'|'V'` (`leave`→`'V'`, `holiday` not marked).
- **LeaveRequestDTO**: rename `from`→`from_date`, `to`→`to_date`; **add** `requester_id`,
  `decided_note`; `type` becomes canonical union.
- **AnnouncementDTO**: **add** `role`, `audience`.
- **Bus/Boarding** (`bus.repo.ts`): `BusStop` ordering field → `seq`; bus number field → `bus_no`;
  `BusPosition`/`BoardingRecord` DTOs keyed to canonical `trip_ping`/`boarding` shapes.
- **Routes** (`*.repo.ts`): align resource names to canonical (`exams`→`exam-papers` where the
  resource is a paper; nested `/classes/{id}/students`, `/exam-papers/{id}/grades`).
- **Tests:** update `src/data/http/__tests__/*` and `src/__tests__/contracts/contract.ts` to the new
  DTO shapes.

### sms-staff  (`src/data/http/mappers.ts`, `src/data/http/*.repo.ts`, tests)

- **RouteDTO**: rename `assigned_bus_no`→`bus_no`; `StopDTO` already canonical (`seq`, `eta_min`).
- **StaffDTO**: already canonical (`first_name`, `role_key`, `emp_id`, `duty_post`). **Add** optional
  `category`, `department`, `attendance_pct`, `status`, `avatar_hue` to match CRM Staff (mapper
  ignores extras the domain doesn't use).
- **LeaveRequestDTO**: already `from_date`/`to_date` ✓; widen `type` to canonical union.
- **TenantDTO**: `logo_url` ✓ (no change).
- **Tests:** update `src/data/http/__tests__/mappers.test.ts` + `repos.test.ts` and
  `src/data/__tests__/contract.test.ts`.

### sms-student  (`src/services/http/`, `src/api/`, tests)

Currently stubs — **build** the canonical boundary:
- Create `src/services/http/dtos.ts` (canonical snake_case DTOs) + `src/services/http/mappers.ts`
  (`to*` functions to existing domain types in `src/models/index.ts`).
- Implement `httpServices` (replace every `NotImplementedError`) against canonical routes using the
  existing `apiFetch` client.
- Entity coverage: `School`(`logo_url`), `Session` (**add `refresh_token`** — today single `token`),
  `Student` (`studentId`→`admission_no`+`id`, `attnPct`→`attendance_pct`), `Subject`(+`teacher_id`),
  `TodayBlock`, `Homework`(+`assignment_id`), `ExamPaper`, `Grade`, `Announcement`(`when`→`date`,
  +`type`), `Teacher`(dir), `ChatThread`(`last`→`last_message`,`when`→`last_at`,`kid`→`child_id`) +
  `ChatMessage`, `Parent`, `Child`, `ChildToday`, `FeeInvoice`+`FeePayment` (split), `PTMMeeting`,
  `Transport`/`BusStop`(`seq`), `AttendanceDay`/`CheckIn`, `LeaveRequest`(`from_date`/`to_date`).
- Update `AuthProvider`/`client.ts` to store + refresh `access_token`/`refresh_token`.
- Add `src/services/http/__tests__/contract.test.ts`.

### sms-catreadmin  (new `src/api/`)

Prototype with no seam — **introduce** one without touching UI:
- Add `src/api/contracts.ts` — canonical DTOs for `Tenant`(Client superset: `slug`, `country`,
  `logo_url`, `limits`, `mrr`, `health_score`, `gateway`, status union), `Plan`, `Subscription`,
  `Invoice`, `SupportTicket`+`TicketMessage`, `OnboardingItem`, `TeamMember`, `AuditLog`.
- Add `src/api/adapter.js` — thin functions mapping the existing `data.jsx` mock objects into the
  canonical DTO shape (so when a real API is wired, the contract already matches; UI reads adapter
  output, unchanged in structure).
- Add a lightweight contract assertion (plain test script) checking adapter output keys.

---

## Testing (production-grade)

- **Contract tests per app:** assert DTO objects (from mock fixtures mapped through the adapter, or
  static sample DTOs) contain exactly the canonical snake_case keys and valid enum values. This is
  the gate that proves alignment.
- **Mapper round-trip tests:** `toX(sampleDTO)` produces a valid domain object; `fromX` (writes)
  produces canonical snake_case.
- **Existing UI/screen tests:** must stay green — no component changed.
- **CI per repo:** `npm test` green = app aligned.

## Rollout order & definition of done

1. **sms-teacher-app** (most-defined boundary, largest surface) — DTO corrections + tests green.
2. **sms-staff** (small delta) — `bus_no` + leave union + tests green.
3. **sms-student** (build boundary + refresh tokens) — http impls + contract tests green.
4. **sms-catreadmin** (introduce seam) — contracts + adapter + key-check green.

**Per-app DoD:** canonical DTOs match §3B, mappers translate to untouched domain types, contract
test passes, all pre-existing tests still pass.

## Risks & mitigations

- **Hidden field usage in UI** — mitigated: mappers preserve the existing domain shape, so screens
  are unaffected; only DTO/mapper files change.
- **student-app auth change (single token → refresh)** — isolated to `AuthProvider`/`client.ts`;
  covered by an auth test.
- **catreadmin has no test runner** — add a minimal Node assertion script; no framework needed.
- **Two split entities (Exam/ExamPaper, FeeInvoice/FeePayment)** — these are the only places a domain
  type may need a field added; handled in the per-app delta, no UI restructure.

## Non-goals

- No backend implementation (separate plan).
- No UI redesign, no renaming of component-level variables.
- No shared cross-repo types package (explicitly deferred).
