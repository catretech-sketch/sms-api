# AI Global Search — Design Spec

Status: Approved for planning
Date: 2026-08-28
Scope: Architectural (new cross-cutting subsystem)

## 1. Objective

Add one centralized, read-only AI search API (`POST /v1/ai/search`) that lets every existing app
(CRM/Admin, Principal, Teacher, Student, Parent, Staff) ask natural-language questions in English,
Hindi, or Hinglish (including mixed-language) — via text, or via voice already converted to text by
the client — and get answers backed by real data from the existing modules. No new AI logic is
built per-app; every consumer calls the same endpoint.

Non-goals (explicitly out of scope for this iteration):
- Speech-to-text — the backend only ever receives text; STT is a client concern.
- Live/streaming answers (e.g. a live bus tracker via chat) — every response is one request/response
  snapshot, even for domains that have live data underneath (see §6, `BusLocationSearch`).
- The full 20+ intent list from the original ask — see §5 for the MVP catalog and what's deferred.
- Any write/mutation capability. This system can never change data (§8).

## 2. Request Flow

```
POST /v1/ai/search  (JWT required — reuses existing pipeline: JwtBearer → TenantResolutionMiddleware → ITenantContext)
        │
        ▼
AiSearchController (thin, extends ApiControllerBase)
        │
        ▼
AiSearchService.SearchAsync(rawQuery, page, pageSize)
        │
        ├─► 1. AiFeatureGate.EnsureEnabledAsync(tenant)         — 403 if tenant's plan lacks the AI Search feature
        │
        ├─► 2. AiClassificationClient.ClassifyAsync(rawQuery)  — Claude API call → {language, intent, filters}
        │        (untrusted output — schema has no tenantId/userId/role field at all)
        │
        ├─► 3. AiSearchAuthorizationService.Authorize(intent, filters, ITenantContext, role)
        │        — resolves the caller's OWN scope (own studentId, linked children, assigned classes)
        │          from the DB using ITenantContext.UserId — never from the LLM's filters
        │        — rejects intents the caller's role isn't permitted to use
        │        — clamps any class/section/studentId the LLM extracted to the caller's authorized scope
        │
        ├─► 4. IAiIntentHandler (one per intent) calls EXISTING services/repositories with the
        │        authorized, backend-resolved filters — never raw LLM filters — returns typed data
        │
        ├─► 5. AiAnswerTemplateService.Render(intent, language, data) — fixed per-language templates
        │
        └─► 6. AiSearchAuditService.LogAsync(...) — never blocks or fails the response
        │
        ▼
AiSearchResponse
```

Invariant carried through every layer: **the LLM only ever produces a hint (intent + candidate
filters). The backend re-derives and clamps every piece of authorization/tenant scope from
`ITenantContext` and existing linkage tables before any repository call.**

## 3. API Contract

```
POST /v1/ai/search
Authorization: Bearer <jwt>

Request:
{
  "query": "Aaj kitne bachche school aaye?",
  "page": 1,       // optional, default 1
  "pageSize": 20   // optional, default 20, max 100 — list-type intents only
}
```

Success, summary-type intent:
```json
{
  "success": true,
  "language": "hinglish",
  "intent": "DailyAttendanceSummary",
  "answer": "Aaj 842 mein se 781 bachche school aaye hain.",
  "data": { "totalStudents": 842, "present": 781, "absent": 61, "attendancePercentage": 92.76 },
  "page": 1, "pageSize": 20, "count": 1, "hasNextPage": false
}
```

Success, list-type intent:
```json
{
  "success": true,
  "language": "en",
  "intent": "StudentSearch",
  "answer": "Found 3 students matching \"Rahul\".",
  "data": [ { "studentId": "...", "name": "...", "className": "...", "section": "...", "attendancePct": 91.2 } ],
  "page": 1, "pageSize": 20, "count": 3, "hasNextPage": false
}
```

Write attempt detected:
```json
{
  "success": true,
  "language": "hinglish",
  "intent": "WriteBlocked",
  "answer": "Main sirf data search aur display kar sakta hoon. Main school data ko modify nahi kar sakta.",
  "data": null, "count": 0
}
```

Unsupported / ambiguous:
```json
{
  "success": true,
  "language": "en",
  "intent": "Unsupported",
  "answer": "I couldn't understand that as a supported search. Try asking about attendance, students, exams, homework, subjects, or bus location.",
  "data": null, "count": 0
}
```

Infra/error (LLM timeout, malformed classification, feature not enabled, invalid request):
```json
{
  "success": false,
  "language": null, "intent": null, "answer": null,
  "error": { "code": "AiSearchUnavailable" | "FeatureNotEnabled" | "InvalidRequest", "message": "..." }
}
```

Rules:
- All responses are HTTP 200 except auth failures (401/403 from the normal pipeline) and request
  validation (400 for empty query / query over `MaxQueryLength`).
- `WriteBlocked`, `Unsupported`, and `Forbidden` (role/scope rejection) are real `intent` values, not
  exceptions — this keeps them directly testable and auditable like every other intent.
- No SQL text, schema names, stack traces, or internal IDs beyond what existing list endpoints
  already expose are ever returned.

## 4. Language & Intent Detection

`AiClassificationClient` (new, `Sms.Application/Services/AiSearch/`) wraps a single call to the
Anthropic Claude API via a named `HttpClient` ("claude"), following the existing external-gateway
pattern used for Razorpay. Config: `AiSearchOptions` bound to an `"AiSearch"` appsettings section —
`ApiKey`, `Model`, `BaseUrl`, `TimeoutSeconds` (short, e.g. 8s), `MaxQueryLength` (e.g. 300 chars,
enforced before the LLM is ever called).

Claude is forced into a structured tool-use response matching:
```json
{
  "language": "en" | "hi" | "hinglish",
  "intent": "<one of the MVP intent enum, or Unsupported, or WriteRequestDetected>",
  "filters": {
    "studentName": "string | null",
    "className": "string | null",
    "section": "string | null",
    "dateExpression": "string | null",
    "targetSelf": "boolean"
  }
}
```

The system prompt is the read-only / no-authorization-override instruction (adapted from the
original spec's §26), scoped to the actual MVP intent enum, with few-shot examples spanning en/hi/
hinglish/mixed-language phrasing for every intent in §5.

Backend never trusts: any tenant/user/role field (the schema has none). `dateExpression` (today/aaj/
kal/is week/etc.) is resolved server-side by a small deterministic `DateExpressionResolver`, never
computed by the LLM.

Failure handling: timeout / error / malformed JSON → `intent: "Unsupported"`, logged, still
`success:true` with a generic "couldn't understand" answer — unless it's a hard infra failure
(e.g. connection refused), in which case `success:false, error.code:"AiSearchUnavailable"`.

## 5. MVP Intent Catalog

| Intent | Who can use it | Existing service/repository reused |
|---|---|---|
| `DailyAttendanceSummary` | Admin/Principal (school-wide); Teacher (own class only) | `PeriodAttendanceQueryRepository`, `AttendanceService` |
| `ClassAttendance` / `SectionAttendance` | Admin/Principal/Teacher (own class only) | same, filtered by class/section |
| `StudentAttendance` | Self (student), linked parent, admin/principal/teacher-of-class | `StudentRepository` live `AttendancePct` |
| `TeacherAttendance` | Admin/Principal; Teacher (self only) | `AttendanceService.GetSummaryAsync` |
| `StaffAttendance` | Admin/Principal; Staff (self only) | staff check-in path |
| `DashboardSummary` | Admin/Principal only | composes the above three summaries |
| `StudentSearch` / `StudentDetails` | Admin/Principal/Teacher (own classes); parent/student limited to own authorized scope | `SisService.ListStudentsAsync` |
| `TeacherSearch` / `StaffSearch` | Admin/Principal only | existing staffing repositories |
| `UpcomingExamSearch` | Self/parent (own class), teacher (own class), admin/principal (school-wide) | `ExamRepository.ListExamsAsync()` + new server-side `FromDate >= today` filter |
| `TestSearch` | same as `UpcomingExamSearch` | **same handler** — no separate "Test" entity exists in the schema; treated as a synonym |
| `HomeworkSearch` | Self/parent (own child), teacher (own class) | `HomeworkRepository.ListAsync` (per-student) / `AssignmentRepository.ListAsync` (per-class) |
| `SubjectSearch` | Self/parent/teacher/admin, all class-scoped | `IAcademicsService.ListSubjectsForStudentAsync` / `ListSubjectsAsync` |
| `BusLocationSearch` | Parent (own children only), admin/staff (by bus/route) | `StudentBusService.GetMyChildrenBusAsync` / `BusModule.GetPositionAsync` — **one-shot snapshot only**, no SignalR/streaming through this endpoint; still gated by the existing `GpsAllowed`/`TransportGps` feature flag in addition to the AI Search gate |
| `WriteBlocked` | anyone | terminal — never reaches a handler |
| `Forbidden` | anyone | terminal — role/scope rejected the intent |
| `Unsupported` | anyone | terminal — fallback for anything outside this catalog (Fee/PTM/Timetable/etc. from the original full list are deferred, not built) |

Each intent maps to one `IAiIntentHandler`, registered in DI by intent name, receiving only the
**authorized** filters object from §6 — never the raw LLM output.

Deferred to a later iteration (explicitly not built now, per YAGNI): `FeeSearch`, `ResultSearch`,
`PTMSearch`, `TimetableSearch`, `ClassSearch`/`SectionSearch` as standalone list intents,
`ParentSearch`. Adding one later means: add a handler, add a role-access row, add prompt few-shot
examples — the pipeline does not change.

## 6. Authorization & Scope-Clamping

`AiSearchAuthorizationService` is the single choke point every request passes through after
classification and before any handler runs. It reuses the exact IDOR-hardening patterns already
proven in `SisService`/`ParentController` (see commits `5a85d01`, `e6548e6`) rather than inventing
new authorization logic:

- **Student self-access** (`targetSelf` or "my attendance"/"meri attendance"): resolve the caller's
  own `studentId` from `ITenantContext.UserId` via the existing self-lookup path. Any `studentName`/
  `studentId` the LLM extracted is ignored for self-referential queries.
- **Parent scope**: resolve authorized children via `SisService.ListLinkedToParentAsync` /
  `IsLinkedToCallerAsync`. If the LLM's `studentName` doesn't match a linked child, the response is
  the standard "no matching records" shape — never "found but forbidden" (no existence leak).
- **Teacher scope**: resolve assigned classes/sections via `TimetableRepository`'s teacher-join
  query. Any `className`/`section` filter is intersected with this set.
- **Admin/Principal/school-wide intents**: gated first by declarative `[Authorize(Policy=...)]` on
  the controller action (existing `Policies.SchoolAdmin`/`Policies.Principal`), then this service
  adds finer per-section scoping (e.g. `DashboardSummary` only shows sections the role is permitted).
- **Role/intent matrix**: a static `AiIntentAccessRules` table (intent → allowed roles), checked
  before dispatch. An unauthorized combination returns intent `Forbidden` (logged distinctly from
  `Unsupported`, so audits can tell "we don't support this" apart from "you can't see this").

## 7. Answer Templates

`AiAnswerTemplateService` renders the final `answer` string from a fixed per-(intent, language)
template — a `Dictionary<(string intent, string lang), Func<TData, string>>` (or resx-per-locale
equivalent), no external i18n library needed for 3 languages across ~15 intents. Numbers are
interpolated as-is from the handler's typed result; the LLM never sees or touches computed values —
this guarantees the data displayed can never diverge from the data in the `data` field.

## 8. Read-Only Guarantee

This is enforced architecturally, not just by convention:
- `IAiIntentHandler` implementations are constructed with only **query** repositories/services via
  DI — no handler has a reference to any `Execute*`/write-capable repository method.
- The classification schema has no path to express "write" as a filter; if the LLM emits
  `intent: "WriteRequestDetected"` (prompted to do so when the query is a mutation like "mark Rahul
  present" or "delete all students"), it maps straight to the `WriteBlocked` terminal response.
- No dynamic SQL is used anywhere in this feature — every handler goes through existing
  parameterized repository methods (Dapper, per `BaseRepository` conventions already in the repo).

## 9. Feature Gating

New `FeatureCatalog` entry (`AiSearch`), checked via the existing `ITenantFeatureSet`/
`RequireFeatureAttribute` mechanism — the same pattern as `StaffCheckInAllowed`. Tenants without it
on their plan get the standard feature-gate 403 before any classification call is made (so ungated
tenants never generate LLM cost).

## 10. Audit Logging

New `AiSearchLog` table + `AiSearchLogRepository` (Dapper, `BaseRepository` conventions):
`UserId, TenantId, Role, Question, DetectedLanguage, DetectedIntent, Timestamp, ResultCount, Success`.
Written via `AiSearchAuditService.LogAsync` in a `try/finally` so a logging failure never breaks the
response. A dedicated table is used rather than the existing `dbo.AuditLog` because that table's
`Action/Target/Kind` shape is oriented around admin actions on entities, not free-text NL queries
with language/intent metadata.

## 11. Rate Limiting & Result Limits

- Reuses the existing `RateLimiting` middleware/config; adds an `AiSearchPermitPerMinute` entry so a
  single user cannot drive unbounded Claude API cost through this endpoint.
- `pageSize` defaults to 20, capped at 100, enforced server-side regardless of what's requested.
- Every list-type handler queries via `COUNT`/aggregate or a bounded, indexed `LIMIT`-style fetch —
  never loads a full table into memory to compute a count in-process (§21 of the original ask).

## 12. Security Tests

New `tests/Sms.Tests.Integration/AiSearch/` suite, following the existing integration test harness
patterns (`ParentChildrenTests`, `LeaveApprovalsTests`). Classification is faked via a test double
`IAiClassificationClient` returning canned `{language, intent, filters}` per test — this isolates
"is our authorization/data layer correct" from "does Claude classify well" (the latter validated
manually/separately, not by automated tests hitting the live API):

1. Tenant isolation — Tenant A's JWT querying class attendance never returns Tenant B rows.
2. Parent can only get data for linked children; unlinked student name → no-match, not another
   parent's child.
3. Student self-access only — asking about another student by name never resolves to them.
4. Teacher scope — teacher querying a class they don't teach → `Forbidden`/no-result.
5. RBAC — a role without an intent's permission (e.g. Staff asking `DashboardSummary`) → `Forbidden`.
6. Write-protection — "mark Rahul present", "delete all students", `'; DROP TABLE Students--` all
   resolve to `WriteBlocked`/`Unsupported`; assert no write-capable repository method is ever
   invoked (structurally true per §8, verified here as a regression guard).
7. Feature gating — tenant without the AI Search feature flag → 403, and asserted that no LLM call
   is attempted (via a call-count assertion on the fake classification client).
8. Bus location — parent querying bus location for a non-linked child → no-match; parent querying
   their own linked child → returns the same data `StudentBusService.GetMyChildrenBusAsync` would.

## 13. Backend Structure

```
Sms.Application/Services/AiSearch/
  IAiSearchService.cs, AiSearchService.cs
  AiClassificationClient.cs (+ IAiClassificationClient.cs)
  AiSearchAuthorizationService.cs
  AiAnswerTemplateService.cs
  AiSearchAuditService.cs
  DateExpressionResolver.cs
  AiIntentAccessRules.cs
  Handlers/
    DailyAttendanceSummaryHandler.cs, ClassAttendanceHandler.cs, StudentAttendanceHandler.cs,
    TeacherAttendanceHandler.cs, StaffAttendanceHandler.cs, DashboardSummaryHandler.cs,
    StudentSearchHandler.cs, TeacherSearchHandler.cs, StaffSearchHandler.cs,
    UpcomingExamSearchHandler.cs, HomeworkSearchHandler.cs, SubjectSearchHandler.cs,
    BusLocationSearchHandler.cs
  IAiIntentHandler.cs

Sms.Api/Controllers/AiSearchController.cs

(new module tables/migrations)
db/Sms.Migrations/M0147_AiSearchLog.cs (or next available number)
```

Config: `AiSearchOptions` in `Sms.Shared.Kernel` (or `Sms.Application`, matching where
`RazorpayOptions` lives), bound to `appsettings`'s `"AiSearch"` section; `ApiKey` supplied via
environment variable override in non-dev environments, per the existing secrets convention.

## 14. Open Items for Implementation Planning

- Exact migration number for `AiSearchLog` (depends on what else lands on `main` before this).
- Exact Claude model name/version to pin in `AiSearchOptions.Model` default.
- Whether `AiSearchPermitPerMinute` needs a distinct per-tenant vs per-user limit (existing
  `RateLimiting` config precedent should be checked at implementation time).
