# SMS Platform — Unified .NET 10 Backend API (Phase-wise Design Plan)

## Context

Five frontend apps already exist under `D:\SMS\sms-project`, each fully built with a
**swappable mock→HTTP data layer** that is currently running on mock data:

| App | Stack | Audience | Backend status |
|-----|-------|----------|----------------|
| `sms-catreadmin` | React (CDN prototype) | SaaS owner (Catre) — tenants, plans, billing, support | mock only |
| `sms-admin` | React 19 + Vite + TS | School admin CRM (owner + school console) | mock (`RestApi` stub) |
| `sms-teacher-app` | Expo RN | Teacher + Principal | mock/HTTP repos, stubs |
| `sms-staff` | Expo RN | 6 worker roles (driver/conductor/guard/gardener/sweeper/peon) | mock/HTTP repos, stubs |
| `sms-student` | Expo RN | Student + Parent (multi-child) | mock services, HTTP stubs |

There is **no backend yet (greenfield)**. The frontends already pin the contract:
Bearer access token + refresh, `X-Tenant-Id` header, **snake_case DTOs**, ISO-8601 timestamps,
RESTful routes (`GET /resources`, `POST /resources/{id}/action`, nested `/classes/{id}/students`).

**Goal:** one production-grade, horizontally-scalable **.NET 10** Web API backed by **SQL Server**,
multi-tenant (shared DB), built with **Dapper** (no EF Core), delivered in phases — **Catre
super-admin first** — that every app flips to by setting `DATA_SOURCE=live` + `API_BASE_URL`.

**Locked decisions:** .NET 10 · Dapper · Shared DB + `TenantId` · cloud-agnostic (Docker) · Catre first.

---

## 1. Architecture — Modular Monolith (Clean layering, Dapper)

One deployable ASP.NET Core Web API. Internal **modules per bounded context**, each isolated so it
can later be peeled into its own service without rewrites. No module references another module's
internals — they talk through published contracts/interfaces.

```
src/
  Sms.Api                 # ASP.NET Core host: program, middleware, endpoint mapping, DI composition
  Sms.Shared.Kernel       # cross-cutting: Result<T>, error contract, paging, ITenantContext, clock,
                          #   IDbConnectionFactory, base repository, auth primitives, snake_case helpers
  Sms.Modules.Identity    # users, roles, RBAC, auth (JWT+refresh), phone/OTP, tenant resolution
  Sms.Modules.Tenancy     # tenants/clients, plans, subscriptions, invoices, billing, onboarding (CATRE)
  Sms.Modules.Sis         # students, parents, guardians, enrolment
  Sms.Modules.Staffing    # teachers, non-teaching staff, HR, payroll, leave, approvals
  Sms.Modules.Academics   # classes, subjects, timetable, exams, grades, assignments, report cards
  Sms.Modules.Attendance  # roll-call + geofenced staff check-in (SESSION_CONTEXT verified)
  Sms.Modules.Finance     # fee structures, invoices, ledger, payments/mandates (gateway)
  Sms.Modules.Transport   # buses, routes, stops, live trips/GPS, boarding
  Sms.Modules.Comms       # chat threads/messages, announcements, complaints, notifications, PTM
  Sms.Modules.Reporting   # cross-school + school analytics, exports
db/
  migrations/             # ordered, idempotent SQL migrations (FluentMigrator) incl. RLS policies
tests/
  Sms.Tests.Unit          # domain + handler tests
  Sms.Tests.Integration   # Testcontainers SQL Server, per-endpoint contract tests
  Sms.Tests.Contract      # asserts response shape == frontend DTO expectations
```

Each module internally follows: `Endpoints → Handlers (use-cases) → Repositories (Dapper SQL) → SQL Server`.
**Minimal APIs** grouped per module via `MapGroup("/v1/...")`. Handlers return `Result<T>`; a single
mapping layer turns that into HTTP + the standard error envelope.

---

## 2. Data layer (Dapper, no EF Core)

- **`IDbConnectionFactory`** opens a `SqlConnection`; on open it sets `SESSION_CONTEXT` keys
  `TenantId` and `UserId` from `ITenantContext` (resolved per request from JWT + `X-Tenant-Id`).
- **Repositories** own hand-written, parameterised SQL (Dapper `QueryAsync`/`ExecuteAsync`).
  Parameterised only — no string concatenation (SQL-injection safe).
- **Multi-tenancy enforcement — defence in depth:**
  1. **SQL Server Row-Level Security**: a security policy with a predicate
     `TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)` on every tenant-scoped
     table. Even a bare `SELECT * FROM Students` returns only the caller's tenant. This is the
     safety net that makes "forgot the WHERE clause" impossible.
  2. Repositories still pass `TenantId` explicitly for index efficiency.
  3. Catre super-admin uses a **platform role** that bypasses RLS (impersonation is audited).
- **Migrations:** **FluentMigrator** — versioned, ordered, idempotent, runs on startup in dev / via
  CI job in prod. Includes table DDL, indexes, and RLS policy creation. (Dapper has no migrations.)
- **IDs:** `uniqueidentifier` (sequential GUID / `NEWSEQUENTIALID` default) for tenant-safe,
  non-enumerable keys exposed to clients. Money as `decimal(18,2)`; timestamps `datetime2` UTC.
- **Performance:** correct covering indexes (esp. `(TenantId, ...)`), keyset pagination for lists,
  Dapper multi-mapping for joined reads, `MARS` off, connection pooling on.

---

## 3. Cross-cutting foundations (Phase 0, used by every later phase)

- **Auth:** JWT access token (short-lived) + rotating **refresh token** (hashed, stored, revocable).
  Endpoints `/v1/auth/login`, `/auth/refresh`, `/auth/me`, `/auth/logout` matching the apps.
  Multiple credential types behind one issuer:
  - email + password (teacher/principal, school admin, Catre team) — `PasswordHasher`,
  - studentId + password (student/parent),
  - phone + OTP (staff app) — OTP issue/verify endpoints, pluggable SMS sender (stub first).
- **Authorization:** policy-based RBAC mirroring the frontends' permission matrices
  (Catre: owner/admin/support/sales/finance/analyst; School: admin/principal/vice_principal/teacher;
  Staff: 6 role keys; plus student/parent). Permissions encoded as policies; **server-side is the
  source of truth** — UI gating is UX only.
- **Tier-gating:** subscription tier (silver/gold/platinum) + feature flags enforced by a
  `RequireFeature("attendance.geofence")` filter, driven by the tenant's active plan. Returns 402/403
  with a machine-readable `feature_locked` code so the UI can show the upsell lock.
- **Tenant resolution middleware:** reads `X-Tenant-Id` + JWT tenant claim, validates membership,
  populates `ITenantContext`, feeds `SESSION_CONTEXT`.
- **Standard contracts:** snake_case JSON (configured `JsonNamingPolicy`), ISO-8601 UTC,
  envelope `{ data }` / error `{ error: { code, message, details } }`, cursor pagination
  `{ data, next_cursor }`. A small mapping convention so domain (camel) ↔ DTO (snake) is centralised.
- **Validation:** FluentValidation per request; 422 with field errors.
- **Observability:** Serilog structured logs (tenant/user/correlation id), OpenTelemetry traces +
  metrics, `/health` + `/health/ready` checks.
- **Resilience/scale primitives (cloud-agnostic interfaces, local impls first):**
  `ICache` (in-memory now → Redis later), `IFileStore` (local → Blob/S3), `IMessageBus`
  (in-process channel → Service Bus/SQS), `IRealtimeHub` (SignalR; → Azure SignalR/Redis backplane).
  Swapping to managed services later is config, not code.
- **Real-time (SignalR):** hubs for chat messages, live bus position, attendance feed.
- **Background jobs:** hosted services / queue consumers for OTP/email/SMS dispatch, invoice
  generation, GPS-ping fan-out, report exports.
- **API docs:** OpenAPI/Swagger generated; published spec becomes the apps' contract reference.
- **Security:** rate limiting, CORS allow-list per app origin, secrets via env/Key Vault-style
  provider, audit log table for sensitive actions (suspend, refund, impersonate, grade publish).

---

## 3A. Canonical schema & field-mismatch reconciliation (reference = `sms-admin`)

The five apps disagree on field names, ID types, enum values, and even entity *meaning* for the
same concept. The backend defines **one canonical schema**; each app's existing fields are mapped to
it. **`sms-admin/src/types/index.ts` is the reference** — where it conflicts with a mobile app, the
admin shape wins, and the app's data layer (mappers/DTOs) is adjusted to match. Two concepts that
*look* like one entity but are structurally different are **split into two tables** (flagged ⚠).

**Global normalization rules (apply to every entity):**
- All JSON is **snake_case**; all IDs are **GUID strings** (fixes `Thread.id`/`FeePayment.id` which
  are numbers in `sms-admin`).
- Attendance percentage field is always `attendance_pct` (admin `attendance`, student `attnPct`).
- `initials` is **derived server-side** from `name` (teacher/student apps send it; admin uses
  `avatarHue`) — both `initials` and `avatar_hue` are returned for convenience.
- Real foreign keys (`class_id`, `student_id`, `tenant_id`) + denormalized display labels
  (`class_label`, `grade`, `section`) so list screens need no extra joins.
- Date ranges use `from_date`/`to_date`; timestamps are ISO-8601 UTC.

### Entity-by-entity reconciliation

**Tenant / School / Client** — *same entity, three names.* Canonical name: **Tenant**.
Catre's `Client` is the superset (billing); `sms-admin.School` is the school-console view; mobile
`Tenant`/`School` are minimal projections.
| Concern | admin (`School`) | catre (`Client`) | mobile | Canonical |
|---|---|---|---|---|
| logo | `logo` | `logo` | `logoUrl` | `logo_url` |
| timezone | `tz` | — | — | `timezone` |
| status | active/trial/past_due | +suspended/cancelled | — | **union** of all 5 (Catre owns lifecycle) |
| slug/country | — | `slug`,`country` | — | `slug`, `country` (add to schema) |
| plan/tier/mrr/limits | partial | full | — | full Catre set |

**Student** ⚠ — identity + roll-type mismatch.
| Field | admin (REF) | teacher-app | student-app | Canonical |
|---|---|---|---|---|
| id | `id`+`adm` | `id` | `studentId` (no id!) | `id` (GUID) + `admission_no` |
| roll | `number` | **`string`** | `number` | `roll` **string** (preserve leading zeros) — teacher already string |
| class | `grade`+`section`+`cls` | `classId` | `grade`+`classroom` | `class_id` FK + `grade`,`section`,`class_label` |
| guardian | `guardian`+`phone`+`father`/`mother` | `parent`+`parentPhone` | (Parent entity) | structured `father`/`mother` + denorm `guardian_name`/`guardian_phone` |
| attendance | `attendance` | `attendance` | `attnPct` | `attendance_pct` |
| `overallAvg`/`rank`/`rankOf` (student-app) | — | — | present | **computed endpoints**, not columns |

**Exam** ⚠ — *two different entities sharing a name.* `sms-admin.Exam` is an **exam term/campaign**
(spans grades + subjects, has `marksEntered`/`published`); teacher/student `Exam` is a **single
paper** (one subject, date, `maxMarks`). Admin already has `PaperSlot` for the paper.
→ Canonical split: **`Exam`** (term — admin shape) **+ `ExamPaper`** (paper — = admin `PaperSlot`,
the teacher/student shape).
| Field | admin term | teacher paper | student paper | Canonical (`ExamPaper`) |
|---|---|---|---|---|
| title | `name` | `title` | `title` | `name` |
| duration | — | `duration` (num min) | `dur` (**string**) | `duration_min` (number) |
| max marks | — | `maxMarks` | `max` | `max_marks` |
| status | scheduled/completed/marks_entry/draft | upcoming/completed/draft | upcoming/graded | **union enum**, mapped per role |

**Attendance** ⚠ — two domains: **student roll-call** (teacher marks) vs **staff/teacher geofenced
check-in**. Kept as separate tables.
| Field | teacher (`AttendanceRecord`) | student (`AttendanceDay`) | Canonical (roll-call) |
|---|---|---|---|
| status | `P`/`A`/`L`/`V` | present/absent/late/off/future | `present`/`absent`/`late`/`leave`/`holiday` (map P→present, V→leave; `future` is UI-derived) |
| key | `studentId`+`date` | `d` (day-of-month) | `student_id`+`date` |

**LeaveRequest** — field names + enum diverge across all three apps.
| Field | teacher-app | staff | student-app | admin (HR balances) | Canonical |
|---|---|---|---|---|---|
| dates | `from`/`to` | `fromDate`/`toDate` | `from`/`to` | — | `from_date`/`to_date` |
| type | casual/sick/emergency/other | casual/sick/earned | — | medical/casual/sick/maternity | **union**: casual,sick,earned,medical,maternity,emergency,other |
| extra | `substitute`,`appliedOn` | balances | `note`,`childId` | balances | keep all as optional; add `LeaveBalance` table |

**Announcement** — `when` vs `date`, missing `type`.
admin/teacher `date` + `type`(info/warning/event/urgent) is canonical; student-app `when`→`date`,
add `type`; keep optional `role`, `pinned`.

**Chat** — Thread vs Contact, message shape differs.
Canonical: **`ChatThread`** {id, name, role, last_message, last_at, unread, group, child_id?} +
**`ChatMessage`** {id, thread_id, sender_id, text, sent_at, is_mine(computed)}. Reconciles
admin `Thread.msgs[{me,t,at}]`, teacher `{isMe,senderId,text,time}`, student `{from,text,time}`.

**Bus / Transport** ⚠ — four shapes. Canonical: **`Bus`** {id, bus_no, label, capacity, fuel,
status} + **`Route`** + **`BusStop`** {id, name, lat, lng, **`seq`**, eta} + **`Trip`**/`TripPing`
(live position) + **`Boarding`**.
| Concern | admin | teacher | staff | student | Canonical |
|---|---|---|---|---|---|
| bus number | `no` | `number` | `assignedBusNo` | `busNo`/`plate` | `bus_no` |
| stops | count (number) | `BusStop[]` (`order`) | `Stop[]` (`seq`) | `nextStops[]` | `BusStop[]` w/ `seq`; count derived |
| live pos | `speed`/`eta` | `BusPosition` | `TripPing` | `eta` | from `TripPing` stream |

**Teacher** — `desig` vs `title`, `subjects[]` vs `subj`. Canonical = admin rich `Teacher`
(`designation`, `subjects[]`, `department`); chat/directory get a lightweight projection
(`{id,name,initials,subject,online}`).

**Staff (non-teaching)** — `role`+`cat` vs `roleKey` enum.
Canonical: `role_key` enum (driver/conductor/guard/gardener/sweeper/peon) + `category`
(transport/security/...) + `emp_id` (add to admin) + `duty_post`,`timing`,`shift`. `firstName` derived.

**Fee** ⚠ — invoice vs payment conflated. Canonical split: **`FeeInvoice`** {period, due_date,
amount, items[], status(due/paid)} (student-app shape) + **`FeePayment`** {amount, `method`, ref,
date, fee_type} (admin shape; `mode`→`method`).

**Session/Auth** — student-app uses a single `token` with no refresh; everyone else uses
`accessToken`+`refreshToken`. Canonical: `{access_token, refresh_token, user, tenant}` — student-app
adopts refresh tokens.

**Approval** — admin generic inbox `{module,cap,forRoles}` vs teacher typed
`{leave|attendance_correction, status, decidedNote}`. Canonical: generic `Approval`
(admin shape) **+** decision fields (`status`, `decided_note`, `decided_by`) + `requester_id`.

> **Action for the frontend apps:** each app keeps its UI types but updates its **DTO/mapper layer**
> (`src/data/http/*`, `src/services/http/*`, `src/lib/api.ts`) to the canonical snake_case contract.
> This mapping work is included in each phase's "flip to live" step. The split entities (Exam/Paper,
> FeeInvoice/Payment, roll-call vs check-in) are the only places an app may need a small UI tweak.

---

## 3B. Canonical data dictionary — every module's fields aligned to School Admin CRM

This is the **authoritative field list** the backend exposes. School Admin CRM (`sms-admin`) is the
master; each canonical field below is the admin field (renamed to snake_case) plus any union fields
needed by other apps. Legend per app column: **✓** = already has it · **→x** = app calls it `x`,
must map · **ADD** = app is **missing** this field and must add it to its DTO · **—** = not used by
that app. "App" columns: **CA**=catre, **AD**=admin(ref), **TE**=teacher, **ST**=staff, **SU**=student/parent.

### Tenant  *(admin `School` + catre `Client` superset)*
| Canonical field | Type | CA | AD | TE | ST | SU |
|---|---|---|---|---|---|---|
| id | guid | ✓ | ✓ | ✓ | ✓ | ✓ |
| name | string | ✓ | ✓ | ✓ | ✓ | ✓ |
| slug | string | ✓ | ADD | — | — | →shortName |
| city | string | ADD | ✓ | — | — | — |
| country | string | ✓ | ADD | — | — | — |
| status | enum(active/trial/past_due/suspended/cancelled) | ✓ | ✓(3) | — | — | — |
| plan_id / tier | guid / enum | ✓ | →plan | — | — | — |
| mrr | decimal | ✓ | ✓ | — | — | — |
| students_count / staff_count | int | ✓ | ✓ | — | — | — |
| limits {students,staff,storage_gb} | json | ✓ | ADD | — | — | — |
| currency | string | ADD | ✓ | — | — | — |
| timezone | string | ADD | →tz | — | — | — |
| logo_url | string | ✓ | →logo | →name only | →logoUrl | →logoUrl |
| color | string | ✓ | ✓ | — | — | — |
| contact {name,email,phone} / csm / health_score / gateway | json | ✓ | ADD | — | — | — |

### Student
| Canonical field | Type | AD(ref) | TE | SU |
|---|---|---|---|---|
| id | guid | ✓ | ✓ | →studentId |
| admission_no | string | →adm | ADD | →studentId |
| name | string | ✓ | ✓ | ✓ |
| initials | string(derived) | ADD | ✓ | ✓ |
| gender | enum(M/F) | ✓ | ADD | ADD |
| class_id | guid | ADD | ✓ | ADD |
| grade / section / class_label | string | ✓/✓/→cls | ✓/ADD/ADD | ✓/ADD/→classroom |
| roll | string | →number | ✓ | →number |
| guardian_name / guardian_phone | string | →guardian/→phone | →parent/→parentPhone | (Parent) |
| father / mother (ParentInfo) | json | ✓ | ADD | ADD |
| attendance_pct | number | →attendance | ✓ | →attnPct |
| fee_status / fee_due | enum / decimal | ✓ | ADD | ADD |
| status | enum(active/inactive) | ✓ | ADD | ADD |
| house | string | ✓ | ADD | ✓ |
| avatar_hue | int | ✓ | ADD | ADD |
| email / dob / blood_group / aadhaar / address / academic_year / admission_date / documents | misc | ✓(optional) | ADD | partial |
| overall_avg / rank / rank_of | number(computed) | (Report ep) | — | ✓ |

### Teacher
| Canonical field | Type | AD(ref) | TE(`User`) | SU(dir) |
|---|---|---|---|---|
| id, name | guid,string | ✓ | ✓ | ✓ |
| initials | string(derived) | ADD | ✓ | ✓ |
| gender | enum | ✓ | ADD | — |
| department | string | →dept | ADD | — |
| designation | string | →desig | →title | — |
| subjects | string[] | ✓ | ADD | →subj(single) |
| class_teacher | guid? | ✓ | →classroom | — |
| phone / email | string | ✓ | ✓ | ADD |
| exp / rating / result / load | number | ✓ | ADD | — |
| attendance_pct | number | →attendance | ADD | — |
| status / avatar_hue / top | misc | ✓ | ADD | — |
| online | bool | ADD | ADD | ✓ |
| employee_no / joined | string | →username | →employee/joined | — |
| onboarding (bank, emergency, transport, docs, etc.) | json | ✓(optional) | — | — |

### Staff (non-teaching)
| Canonical field | Type | AD(ref) | ST |
|---|---|---|---|
| id, name | guid,string | ✓ | ✓ |
| first_name | string(derived) | ADD | ✓ |
| role_key | enum(driver/conductor/guard/gardener/sweeper/peon) | ADD(has free `role`) | ✓ |
| category | enum(transport/security/...) | →cat | ADD |
| emp_id | string | →username | ✓ |
| department | string | →dept | ADD |
| phone | string | ✓ | ✓ |
| shift / duty_post / timing | string | ✓shift / ADD / ADD | ✓ |
| route | string? | ✓ | (via assignment) |
| rating | number | ADD | ✓ |
| attendance_pct / status / avatar_hue | misc | ✓ | ADD |
| joined | iso | ADD | ✓ |
| onboarding (bank, emergency, docs) | json | ✓(optional) | →documents |

### Class · Subject · TimetableSlot  *(admin builds them; teacher/student read)*
- **Class**: `id, tenant_id, name, grade, section, subject?, room, student_count, class_teacher_id, next_period?` — TE `Class` ✓; AD stores grade/section; SU reads label.
- **Subject**: `id, name, short, teacher_id, color` — SU `Subject` ✓ (add `teacher_id`); AD/TE reference by name today → ADD `id`.
- **TimetableSlot**: `id, class_id, day(Mon–Fri), period, subject, room, start_time, end_time` — TE ✓; SU `TodayBlock` maps (`t`→start_time, `label`→subject).

### Exam (term)  +  ExamPaper (single paper)  ⚠ split
- **Exam (term)** = admin `Exam`: `id, name, type, grades, from_date, to_date, subject_count, status(scheduled/completed/marks_entry/draft), marks_entered_pct, published`.
- **ExamPaper** = admin `PaperSlot` ≈ teacher/student `Exam`:
  `id, exam_id, class_id, subject, date, start_time, duration_min, max_marks, room, invigilator1, invigilator2, topics[], status(union)`.
  TE: `title`→name, `duration`✓, `maxMarks`→max_marks, `room/inv*` ADD. SU: `dur`(string)→duration_min(number), `max`→max_marks, `score/grade` come from Grade.

### Grade  *(admin `ReportRow`/`Report` + teacher `GradeEntry` + student `Grade`)*
| Canonical (`Grade`) | Type | AD | TE(`GradeEntry`) | SU(`Grade`) |
|---|---|---|---|---|
| id | guid | (computed) | ADD | ✓ |
| student_id / student_name | guid/string | ✓ | ✓ | ADD |
| exam_paper_id | guid | →subject | →examId | →subjId |
| marks / max_marks | number | →marks/max | ✓/✓ | →score/max |
| grade / gpa / pass | string/number/bool | ✓ | →grade only | →grade |
| date | iso | ADD | ADD | ✓ |

### Assignment *(teacher-owned; student sees as Homework)*
- Canonical `Assignment`: `id, class_id, class_label, subject, title, due_date, due_time, submissions_count, total_students, status(active/due_soon/overdue/closed)`.
- **Homework** (student view) = per-student projection: `id, assignment_id, subject_id, title, due_date, due_time, status(todo/progress/submitted/graded), priority, grade?`. SU `Homework` maps; needs `assignment_id` ADD.

### Attendance (roll-call)  ⚠ vs check-in
- `AttendanceRecord`: `id, tenant_id, class_id, student_id, date, status(present/absent/late/leave/holiday), marked_by`. TE `P/A/L/V`→map; SU `AttendanceDay.kind` (`off`→holiday, `future` UI-only). `AttendanceFlag` (student) = derived view of absent/late records.

### Geofenced check-in  *(teacher `CheckEvent` + staff `AttendanceLog`)*
- `CheckIn`: `id, user_id, kind(in/out), at, lat, lng, accuracy_meters, distance_meters, verified, in_zone`. ST `AttendanceLog` (`inZone`) ✓ + ADD distance/accuracy; TE `CheckEvent` ✓ + ADD `in_zone`. Plus `SchoolLocation`: `lat, lng, radius_meters, name`.

### LeaveRequest  +  LeaveBalance
| Canonical (`LeaveRequest`) | Type | TE | ST | SU |
|---|---|---|---|---|
| id | guid | ✓ | ✓ | ✓ |
| requester_id | guid | ADD | ADD | →childId(parent files for child) |
| type | enum(casual/sick/earned/medical/maternity/emergency/other) | ✓(subset) | ✓(subset) | ADD |
| from_date / to_date | iso | →from/to | →fromDate/toDate | →from/to |
| reason | string | ✓ | ✓ | ✓ |
| substitute / note | string? | ✓ / ADD | ADD / ADD | ADD / ✓ |
| status | enum(pending/approved/rejected) | ✓ | ✓ | ✓ |
| applied_on / decided_note | iso/string | ✓ / ADD | ADD / ADD | ADD / ADD |
- **LeaveBalance**: `user_id, type, total, used` — ST ✓; add for TE/staff via HR.

### Approval *(admin generic inbox + teacher decision fields)*
`id, tenant_id, type, module, cap(V/E/A), title, detail, requester_id, requester_name, role, amount?, priority, status(pending/approved/rejected), for_roles[], applied_on, decided_by?, decided_note?`. AD has `module/cap/forRoles` (ADD status/decided_*); TE has `status/decidedNote/requesterId` (ADD module/cap).

### Announcement
`id, tenant_id, title, body, date, from, role?, type(info/warning/event/urgent), pinned, audience`. TE ✓ (ADD `role`,`audience`); SU `when`→date, ADD `type`,`pinned`.

### ChatThread + ChatMessage
- **ChatThread**: `id, name, role, last_message, last_at, unread, group, child_id?, participant_ids[]`. AD `Thread`(id number→guid, `last`→last_message, `time`→last_at); TE `ChatContact`(`lastMessage`→last_message,`time`→last_at, ADD group); SU `ChatThread`(`last`→last_message,`when`→last_at,`kid`→child_id).
- **ChatMessage**: `id, thread_id, sender_id, text, sent_at, is_mine(derived)`. AD `{me,t,at}`, TE `{isMe,senderId,text,time}`, SU `{from,text,time}` all map.

### FeeInvoice + FeePayment  ⚠ split
- **FeeInvoice** (student bill): `id, student_id, period, due_date, amount, items[{label,amount}], status(due/paid), paid_on?, method?`. SU `Fee` ✓ (`items.l/amt`→label/amount). AD tracks `feeStatus/feeDue` on Student (derived from invoices).
- **FeePayment** (transaction): `id, student_id, student_name, class_label, fee_type(academic/transport/other), amount, method, ref, date`. AD `FeePayment` ✓ (`mode`→method, id number→guid).

### Transport: Bus + Route + BusStop + Trip + TripPing + Boarding
- **Bus**: `id, bus_no, label, capacity, fuel, status, driver_id, conductor_id, route_id`. AD `no`→bus_no, `students/stops/speed/eta` derived; TE `number`→bus_no + `driverPhone` ADD; ST `assignedBusNo`→bus_no.
- **Route**: `id, name, bus_no, stops[]` (ST ✓).
- **BusStop**: `id, route_id, name, lat, lng, seq, eta_min?`. TE `order`→seq; ST ✓; SU `nextStops` maps.
- **Trip / TripPing / Boarding / StudentLite / TripSummary**: adopt staff shapes as canonical (ST ✓); TE `BusPosition`/`BoardingRecord` map onto TripPing/Boarding.

### Catre-only entities  *(no admin reference — Catre is canonical)*
- **Plan**: `id, name, tier, pricing(flat/per_student), price, per_student, min_students, period, features[], limits, visibility(published/draft), audience, offer{label,pct}, band, color, desc`.
- **Subscription**: `id, tenant_id, plan_id, status, started_at, renews_at, seats`.
- **Invoice**: `id, tenant_id, plan_name, amount, status(paid/open/past_due), issued, due, paid_on`.
- **SupportTicket** + **TicketMessage**: `id, subject, tenant_id, status, priority, assignee, created, updated, messages_count`.
- **OnboardingItem**: `id, tenant_id?, name, slug, owner, value, checklist[5], done, age`.
- **TeamMember**: `id, name, email, role(owner/admin/support/sales/finance/analyst), status, last_login, joined`.
- **AuditLog**: `id, actor_id, actor_name, role, action, target, kind, time`.

### Other school entities  *(admin/app reference)*
- **Complaint** (AD): `id, subject, from, category, priority, status(open/in_progress/resolved), age, assignee, body`.
- **Notification** (AD `AppNotification`): `id, icon, tone, title, body, time, unread` (id number→guid).
- **PTMMeeting** (SU): `id, tenant_id, child_id, date, time, teacher, subject, mode, status(confirmed/pending)`.
- **CalendarEvent**: `id, date, title, time?, type(exam/holiday/meeting/event/deadline), description?` — TE ✓; SU `CalendarEvent`(day+items) maps to per-day grouping of these.
- **LibraryBook** (TE): `id, title, author, subject, issued_to?, due_date?, status(available/issued/overdue)`.
- **PayslipEntry** (TE): `id, user_id, month, year, gross, deductions, net, status(paid/pending)`.
- **Achievement / Peer** (SU): `id, ...` — student-app only, canonical = student shapes.

> **Per-app task summary:** every **ADD**/**→rename** cell above is a concrete mapper change in that
> app's HTTP DTO layer, scheduled in the phase that lights up the relevant module. No app loses a
> field; apps only *gain* the canonical fields they were missing and rename the few that differ.

---

## 3C. Cross-app route & contract consistency (verified 2026-06-13)

After aligning DTO fields, a cross-app route/resource audit produced these contract rules the
backend MUST follow so one API serves all apps:

- **Unified auth surface:** `/auth/login`, `/auth/refresh`, `/auth/me`, `/auth/logout` for ALL apps
  (teacher, student, staff). `/auth/login` accepts a polymorphic credential body — email+password
  (teacher/admin), studentId/email+password+role (student/parent), or phone+role_key (staff). Staff
  previously used `/staff/auth/*`; now unified to `/auth/*`. Resource endpoints stay role-namespaced
  (`/staff/dashboard`, `/staff/trips/*`, etc.).
- **Messaging resource is `/threads`** (not `/chats`): `GET /threads`, `GET /threads/{id}/messages`,
  `POST /threads/{id}/messages`. Teacher previously used `/chats`; now unified. Message DTO is
  canonical (`thread_id`, `sender_id`, `text`, `sent_at`, `is_mine`); thread DTO uses `last_message`,
  `last_at`, `unread`.
- **Role-scoped resources are intentionally distinct** (NOT inconsistencies): roll-call attendance
  `/classes/{id}/attendance` vs self check-in `/me/attendance/*` (teacher) / `/staff/attendance/*`
  (staff) vs parent month-view `/children/{id}/attendance`; teacher `/bus/*` (assigned-bus view) vs
  staff `/staff/trips/*` (live trip ops); three leave endpoints `/leave` (teacher), `/staff/leave`
  (staff), `/children/{id}/leave` (parent).
- **ExamPaper** is one shared resource at `/exam-papers`; it carries BOTH `subject_id` (FK, used by
  student) and `subject` (label, used by teacher), plus `name` (paper title, unified — not `title`).
- **Homework** (`/homework`, student per-student view) carries `assignment_id` linking to the
  teacher-created **Assignment** (`/assignments`).
- **Follow-up (client-side, not a backend gap):** the student app does not yet CALL `/auth/refresh`
  or `/auth/me` (its Session DTO already carries `refresh_token`). The backend exposes them uniformly;
  wiring the student client's refresh-on-401 + rehydrate-on-launch is a later client task.

---

## 4. Delivery phases (end-to-end, each independently shippable & verifiable)

Each phase = working endpoints + migrations + tests + the corresponding app flipped to `live` and
verified against its own contract tests. "Definition of done" per phase: app runs on real API with no mocks.

### Phase 0 — Platform foundation *(prerequisite for all)*
Solution skeleton, DI, middleware, `IDbConnectionFactory` + `SESSION_CONTEXT`, FluentMigrator runner,
auth (JWT+refresh, all 3 credential types incl. OTP stub), RBAC policy engine, tier-gating filter,
tenant middleware + RLS scaffolding, error/paging/snake_case conventions, Serilog+OTel, Swagger,
Docker Compose (API + SQL Server), CI build/test. **No business endpoints yet.**

### Phase 1 — Catre super-admin (`sms-catreadmin`) *(first business slice)*
Tables + endpoints for: **Tenants/Clients** (lifecycle: trial→active→suspended→cancelled, usage
metrics, health score), **Plans** (per-student/flat pricing, features, tiers, visibility/offers),
**Subscriptions**, **Invoices** (issue/mark-paid/refund), **Billing gateway/mandate** fields,
**Onboarding pipeline** (Kanban stages + checklists), **Support tickets** (+ threaded messages),
**Internal Team** (6 roles), **Audit log**, **Reports/KPIs** (MRR, signups, plan distribution, CSV).
Platform-role bypass + audited impersonation. → flip `sms-catreadmin` to live.
*Representative routes:* `GET/POST/PUT/DELETE /v1/clients`, `/clients/{id}/usage`, `/clients/{id}/activity`,
`/plans`, `/subscriptions`, `/invoices/{id}/mark-paid|refund`, `/onboarding`, `/tickets`, `/team`, `/reports`.

### Phase 2 — School Admin CRM (`sms-admin`)
Core school entities every other app depends on: **Schools** (tenant settings), **Students (SIS)** +
full enrolment, **Teachers**, **Staff**, **Parents/guardians + linking**, **Academics** (classes,
subjects, timetable), **Exams + grading + report cards**, **Attendance**, **Fees** (structure,
invoices, ledger), **HR/Payroll**, **Communication** (threads, complaints, announcements),
**Operations** (library, transport, hostel, sports), **Approvals inbox**, **Reports**. Owner console
reuses Phase-1 tenancy reads. → flip `sms-admin` to live.

### Phase 3 — Teacher + Principal app (`sms-teacher-app`)
Read/write over Phase-2 entities scoped to the teacher: classes/students, roll-call attendance, marks
& exams (create/patch/delete), assignments, grades upsert, timetable, chat (SignalR), announcements
(principal broadcast), calendar, **geofenced self check-in** (`SESSION_CONTEXT`-verified, distance +
accuracy stored), leave requests, **principal approvals** (leave + attendance corrections),
principal overview/attendance KPIs, assigned bus + live position. → flip `sms-teacher-app` to live.

### Phase 4 — Staff app (`sms-staff`)
6-role dashboards (polymorphic role cards), **geofenced check-in/out**, **live trips** (start/end,
GPS ping ingest + SignalR fan-out, distance/duration summary), **boarding** roster + state, **tasks**
(complete), **leave** (balances + requests). Phone/OTP login. → flip `sms-staff` to live.

### Phase 5 — Student + Parent app (`sms-student`)
Student: profile, today/schedule, subjects, homework (status/submit), grades/exams, announcements,
chat. Parent: multi-child switch, child today/attendance/progress, **fees + online payment**
(gateway), **PTM** booking, **transport** live tracking (reuses Phase-4 trips), leave for child.
→ flip `sms-student` to live.

### Phase 6 — Production hardening & scale
Swap cloud-agnostic interfaces to managed services (Redis cache + SignalR backplane, Blob/S3 files,
Service Bus/SQS), load testing + index tuning, read-replica/caching for heavy reads (KPIs, GPS),
horizontal scale-out (stateless API + distributed cache/SignalR), DR/backup, rate-limit tuning,
penetration test of RLS + RBAC, finalize OpenAPI as published contract.

---

## 5. Critical files / artifacts to create

- Solution + projects per §1 (`Sms.Api`, `Sms.Shared.Kernel`, `Sms.Modules.*`, `tests/*`).
- `Sms.Shared.Kernel`: `IDbConnectionFactory`, `ITenantContext`, `Result<T>`, error envelope,
  `SnakeCaseNamingPolicy`, base repository, paging, auth/JWT helpers, tier-gating filter.
- `db/migrations/*`: FluentMigrator classes — tables, indexes, and **RLS security policies**.
- `Sms.Api/Program.cs`: DI composition, middleware order (auth → tenant resolution → endpoints),
  module endpoint registration via `MapGroup`.
- `docker-compose.yml`: API + SQL Server for local dev parity.
- Per-module: `Endpoints`, `Handlers`, `Repositories`, `Contracts (DTOs)`, validators, tests.

**Contract source of truth (read, don't redefine):**
`sms-admin/src/types/index.ts`, `sms-admin/src/data/mockDb.ts` (tiers/roles/perms),
`sms-teacher-app/src/data/domain/index.ts` + `src/data/http/*.repo.ts`,
`sms-staff/src/data/domain/index.ts`, `sms-student/src/models/index.ts` + `src/services/types.ts`,
`sms-catreadmin/data.jsx`. Each module's DTOs must match these shapes (snake_case).

---

## 6. Verification (per phase)

1. **Integration tests** (xUnit + **Testcontainers SQL Server**): each endpoint hit against a real
   ephemeral SQL Server with migrations applied; assert status, body shape, and **RLS isolation**
   (tenant A cannot read tenant B — explicit negative tests).
2. **Contract tests:** response JSON matches the frontend DTO for that route (snake_case keys, types).
3. **App-level e2e:** set the app's `DATA_SOURCE=live` + `API_BASE_URL` to the local container and
   run its existing `*.contract.test.ts`; confirm the app boots and core flows work with zero mocks.
4. **Auth/RBAC matrix tests:** each role's allowed/denied actions enforced server-side.
5. **Run locally:** `docker compose up` → Swagger UI exercised → app pointed at it.

---

## 7. Defaults chosen for you (flag during spec review if you want changes)

- **ORM helpers:** Dapper + FluentMigrator (migrations) + FluentValidation. Dapper.Contrib avoided
  (keep SQL explicit). *(Confirm FluentMigrator vs DbUp.)*
- **Payments gateway:** abstracted `IPaymentGateway` with an India-first impl assumption
  (UPI mandates/cards — the catreadmin gateway fields imply Razorpay-style). Stubbed until you name a
  provider. *(Confirm provider.)*
- **Auth issuer:** self-hosted JWT (not an external IdP), since the apps expect first-party
  access+refresh tokens. *(Confirm — vs Azure AD B2C/Auth0/Keycloak.)*
- **OTP/SMS + email:** pluggable senders, console/stub impl first; wire a real provider in Phase 6.
- **Hosting:** kept cloud-agnostic (Docker + interfaces) per your "decide later"; Phase 6 binds to the
  chosen cloud.
