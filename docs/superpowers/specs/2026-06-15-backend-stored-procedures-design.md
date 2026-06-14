# SMS Backend — Stored-Procedure Data Layer + Full 7-Phase Roadmap (Design Spec)

> **Status:** Approved design (2026-06-15). This spec is the **delta + execution structure** on top of
> the master design `docs/2026-06-13-backend-api-design.md`. That doc remains authoritative for
> architecture, the canonical data dictionary (§3B), and route/contract rules (§3C). **This spec
> overrides §2 of that doc** (inline SQL → stored procedures where they add value) and defines the
> phase-by-phase build order the implementation plan will follow.

## Context

Greenfield .NET 10 Web API serving five existing frontend apps (`sms-catreadmin`, `sms-admin`,
`sms-teacher-app`, `sms-staff`, `sms-student`), all currently on swappable mock→HTTP data layers.
The contract is already pinned by the frontends: Bearer + refresh tokens, `X-Tenant-Id` header,
**snake_case DTOs**, ISO-8601 UTC, RESTful routes. The canonical schema reconciling all five apps is
fully documented in the master design doc.

**Locked decisions (this spec):**
- .NET 10 · Dapper · SQL Server · shared DB + `TenantId` (multi-tenant) · cloud-agnostic (Docker) · Catre first.
- **Data access via stored procedures *where they add value*** (not a blanket all-SP rule). See §1.
- **FluentMigrator** versions both table DDL and procedures.
- Delivery as **one master plan, 7 sequential phases**, each independently shippable & verifiable.
- **Dev SQL Server:** instance `DESKTOP-TJL4SG6` (connection string wired during Phase 0 execution).

---

## 1. Stored-procedure data layer (replaces master-doc §2 inline-SQL approach)

```
Repository (C#) ──Dapper──► [ SP for writes/complex reads | inline SQL for simple reads ] ──► table (RLS-filtered)
```

### 1.1 When to use a stored procedure vs inline SQL

| Use a **stored procedure** | Keep **parameterised inline SQL** |
|---|---|
| All writes: create / update / delete / state transitions | Simple single-table `GET by id` |
| Multi-table / joined reads | Simple single-table list (one table, basic filter + keyset page) |
| Reporting & dashboard queries (often `QueryMultiple`) | |
| Bulk operations via table-valued parameters (roll-call, GPS pings) | |
| Anything with non-trivial business logic that belongs set-based in the DB | |

Rationale: procedures give cached execution plans, set-based performance, and centralized SQL for the
heavy paths (the "scale data" goal); trivial reads gain nothing from proc overhead.

### 1.2 Conventions

- **Naming:** `{Entity}_{Action}` — `Student_Create`, `Student_Update`, `Student_Delete`,
  `Student_ListByClass`, `Invoice_MarkPaid`, `Dashboard_CatreOverview`, `Attendance_BulkUpsert`.
- **Invocation:** `conn.QueryAsync<T>("dbo.Student_ListByClass", p, commandType: CommandType.StoredProcedure)`
  with Dapper `DynamicParameters`. No string concatenation anywhere (SQL-injection safe).
- **Result mapping:** procs return snake_case-friendly columns; Dapper multi-mapping for joins;
  `QueryMultiple` for dashboard procs returning several lists in one round-trip.
- **Bulk:** SQL Server **table-valued parameters** (TVP) + user-defined table types for batch inserts
  (attendance roll-call, GPS `TripPing` fan-in). One round-trip, set-based.
- **Pagination:** keyset/seek pagination implemented inside the proc (no `OFFSET` scans on large tables).

### 1.3 Multi-tenancy (unchanged, defence-in-depth — still applies through procs)

1. **`IDbConnectionFactory`** sets `SESSION_CONTEXT('TenantId')` and `('UserId')` on connection open,
   from `ITenantContext` (resolved per request from JWT + `X-Tenant-Id`).
2. **Row-Level Security** policy on every tenant-scoped table:
   `TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`. A proc that forgets the tenant
   filter still returns only the caller's rows — the safety net is at the table, not the query.
3. Procs also accept `@TenantId` explicitly for index efficiency.
4. Catre super-admin uses a **platform role** that bypasses RLS; impersonation is audited.

### 1.4 Procedures are versioned migrations (not hand-edited in the DB)

- **FluentMigrator** manages ordered, idempotent migrations for **both** table DDL/indexes/RLS **and**
  `CREATE OR ALTER PROCEDURE` scripts.
- Proc SQL lives in source control under `db/migrations/procs/` (embedded resource per proc), applied
  on startup in dev / via CI job in prod. Re-running is safe (`CREATE OR ALTER`).
- A migration that changes a table's shape ships alongside the migration that updates the affected
  procs, so schema and procs never drift.

### 1.5 Other data-layer rules (carried from master doc, unchanged)

- IDs: `uniqueidentifier` (sequential GUID) exposed to clients. Money `decimal(18,2)`; time `datetime2` UTC.
- Covering indexes on `(TenantId, ...)`; MARS off; connection pooling on.

---

## 2. Architecture (unchanged from master doc §1)

Modular monolith, one deployable ASP.NET Core Web API, internal modules per bounded context
(`Identity`, `Tenancy`, `Sis`, `Staffing`, `Academics`, `Attendance`, `Finance`, `Transport`,
`Comms`, `Reporting`) over `Sms.Shared.Kernel`. Minimal APIs grouped via `MapGroup("/v1/...")`;
handlers return `Result<T>`; one mapping layer to HTTP + standard error envelope. Module layering:
`Endpoints → Handlers → Repositories (Dapper → procs/inline SQL) → SQL Server`.

---

## 3. Cross-cutting foundations (unchanged from master doc §3)

Auth (JWT access + rotating refresh; email/pw, studentId/pw, phone/OTP), policy-based RBAC, tier-gating
(`RequireFeature`), tenant-resolution middleware feeding `SESSION_CONTEXT`, snake_case JSON + ISO-8601 +
`{data}`/`{error}` envelopes + cursor pagination, FluentValidation (422), Serilog + OpenTelemetry +
`/health`, cloud-agnostic resilience interfaces (`ICache`, `IFileStore`, `IMessageBus`, `IRealtimeHub`),
SignalR real-time, background jobs, OpenAPI/Swagger, rate-limiting/CORS/secrets/audit log.

**Canonical schema & routes:** §3A/3B/3C of the master doc are the authoritative field list and
route/contract rules. Every module's DTOs and endpoints conform to them; this spec does not redefine them.

---

## 4. Delivery roadmap — one master plan, 7 sequential phases

Each phase = procs + inline-read repos + migrations (DDL + procs + RLS) + endpoints + tests + the
matching frontend app flipped to `DATA_SOURCE=live` and verified against its own contract tests.
**Definition of done per phase:** the app runs on the real API with zero mocks; RLS isolation proven.

### Phase 0 — Platform foundation *(prerequisite for all)*
Solution skeleton + DI + middleware order (auth → tenant resolution → endpoints); `IDbConnectionFactory`
+ `SESSION_CONTEXT`; **FluentMigrator runner for DDL + procs**; Dapper proc-calling base repository +
inline-read helper; auth (JWT+refresh, all 3 credential types incl. OTP stub); RBAC policy engine;
tier-gating filter; tenant middleware + RLS scaffolding; error/paging/snake_case conventions; Serilog +
OTel; Swagger; **Docker Compose (API + SQL Server)** with dev connection to `DESKTOP-TJL4SG6`; CI
build/test. **No business endpoints.**

### Phase 1 — Catre super-admin (`sms-catreadmin`)
Tenants/Clients (lifecycle trial→active→suspended→cancelled, usage, health score), Plans, Subscriptions,
Invoices (issue/mark-paid/refund), billing/mandate fields, Onboarding pipeline, Support tickets (+threaded
messages), internal Team (6 roles), Audit log, Reports/KPIs (MRR, signups, plan distribution, CSV).
Platform-role RLS bypass + audited impersonation. Dashboard procs via `QueryMultiple`.
→ flip `sms-catreadmin` to live.

### Phase 2 — School Admin CRM (`sms-admin`)
Core entities every other app depends on: Schools (tenant settings), Students (SIS) + enrolment,
Teachers, Staff, Parents/guardians + linking, Academics (classes, subjects, timetable), Exams + grading +
report cards, Attendance, Fees (structure, invoices, ledger), HR/Payroll, Communication (threads,
complaints, announcements), Operations (library, transport, hostel, sports), Approvals inbox, Reports.
→ flip `sms-admin` to live.

### Phase 3 — Teacher + Principal app (`sms-teacher-app`)
Teacher-scoped read/write over Phase-2 entities: classes/students, **roll-call attendance (bulk-upsert
proc + TVP)**, marks & exams (create/patch/delete), assignments, grades upsert, timetable, SignalR chat,
announcements (principal broadcast), calendar, **geofenced self check-in** (`SESSION_CONTEXT`-verified,
distance + accuracy), leave requests, **principal approvals** (leave + attendance corrections),
principal overview/attendance KPIs, assigned bus + live position. → flip `sms-teacher-app` to live.

### Phase 4 — Staff app (`sms-staff`)
6-role dashboards (polymorphic role cards), geofenced check-in/out, **live trips** (start/end,
**GPS-ping ingest via TVP** + SignalR fan-out, distance/duration summary), boarding roster + state,
tasks (complete), leave (balances + requests). Phone/OTP login. → flip `sms-staff` to live.

### Phase 5 — Student + Parent app (`sms-student`)
Student: profile, today/schedule, subjects, homework (status/submit), grades/exams, announcements, chat.
Parent: multi-child switch, child today/attendance/progress, **fees + online payment (gateway)**, PTM
booking, transport live tracking (reuses Phase-4 trips), leave for child. → flip `sms-student` to live.

### Phase 6 — Production hardening & scale
Swap cloud-agnostic interfaces to managed services (Redis cache + SignalR backplane, Blob/S3 files,
Service Bus/SQS); load testing + **index & stored-procedure plan tuning**; read-replica/caching for heavy
reads (KPIs, GPS); horizontal scale-out (stateless API + distributed cache/SignalR); DR/backup;
rate-limit tuning; **penetration test of RLS + RBAC**; finalize OpenAPI as the published contract.

---

## 5. Artifacts to create (per master-doc §5, with proc additions)

- Solution + projects: `Sms.Api`, `Sms.Shared.Kernel`, `Sms.Modules.*`, `tests/*`.
- `Sms.Shared.Kernel`: `IDbConnectionFactory`, `ITenantContext`, `Result<T>`, error envelope,
  `SnakeCaseNamingPolicy`, **proc-calling base repository + inline-read helper**, paging, JWT helpers,
  tier-gating filter.
- `db/migrations/*`: FluentMigrator classes for tables, indexes, RLS policies, **and procedures**
  (`db/migrations/procs/*.sql` as `CREATE OR ALTER`).
- `Sms.Api/Program.cs`: DI composition, middleware order, module endpoint registration via `MapGroup`.
- `docker-compose.yml`: API + SQL Server for local dev parity.
- Per module: `Endpoints`, `Handlers`, `Repositories`, `Contracts (DTOs)`, validators, **procs**, tests.

**Contract source of truth (read, don't redefine):** `sms-admin/src/types/index.ts`,
`sms-admin/src/data/mockDb.ts`, `sms-teacher-app/src/data/domain/index.ts` + `src/data/http/*.repo.ts`,
`sms-staff/src/data/domain/index.ts`, `sms-student/src/models/index.ts` + `src/services/types.ts`,
`sms-catreadmin/data.jsx`. Plus the canonical dictionary (§3B) and route rules (§3C) of the master doc.

---

## 6. Verification (per phase)

1. **Integration tests** (xUnit + **Testcontainers SQL Server**): migrations + procs applied to an
   ephemeral instance; each endpoint asserted for status, body shape, and **RLS isolation** (tenant A
   cannot read tenant B — explicit negative tests).
2. **Contract tests:** response JSON matches the frontend DTO (snake_case keys, types, enums).
3. **App-level e2e:** point the app's `DATA_SOURCE=live` + `API_BASE_URL` at the local container; run
   its existing `*.contract.test.ts`; confirm it boots and core flows work with zero mocks.
4. **Auth/RBAC matrix tests:** each role's allowed/denied actions enforced server-side.
5. **Proc tests:** each write/complex-read proc covered by an integration test (incl. TVP bulk paths).
6. **Run locally:** `docker compose up` → Swagger exercised → app pointed at it (`DESKTOP-TJL4SG6` in dev).

---

## 7. Defaults & open confirmations (flag during review if you want changes)

- **Migrations/procs tool:** FluentMigrator (DDL + procs). *(Alternative: DbUp — not chosen.)*
- **Payments gateway:** abstracted `IPaymentGateway`, India-first (Razorpay-style) assumption, stubbed
  until a provider is named. *(Confirm provider before Phase 5.)*
- **Auth issuer:** self-hosted JWT (first-party access+refresh), not an external IdP. *(Confirm.)*
- **OTP/SMS + email:** pluggable senders, console/stub first; real provider in Phase 6.
- **Dev DB auth:** `DESKTOP-TJL4SG6` — confirm Windows auth vs SQL login + whether a dedicated
  `sms` database/login should be created in Phase 0.

---

## 8. Non-goals

- No frontend rewrites — apps only flip their existing `DATA_SOURCE` switch and adjust DTO/mapper layers
  (covered by the separate per-app field-alignment plans).
- No blanket "all stored procedures" — simple single-table reads stay inline by design.
- No cloud binding until Phase 6 (stays Docker + interfaces).
</content>
</invoke>
