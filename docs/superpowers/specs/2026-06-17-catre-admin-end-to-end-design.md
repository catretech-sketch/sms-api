# Catre Admin End-to-End Readiness — Design

**Date:** 2026-06-17
**Status:** Approved (pending spec review)
**Scope:** Make the Catre platform-admin surface usable end-to-end. Two internal
sub-projects. Payment gateway and SMS OTP are explicitly **deferred** to a later
cycle (they require external vendor decisions + credentials).

---

## Background

"Catre" is the SaaS platform provider; the **Catre admin** is the platform
super-admin who manages client schools, plans, billing, tickets, team, etc. —
not a tenant/school user. Every Catre endpoint (`/v1/clients`, `/v1/plans`,
`/v1/invoices`, `/v1/tickets`, `/v1/onboarding`, `/v1/team`, `/v1/audit`,
`/v1/reports`, `/v1/dashboard`) is gated by `RequireAuthorization("platform")`,
which requires a JWT carrying `is_platform=1`. That claim is only issued for a
`Users` row with `IsPlatform=1`.

A read-only audit of the backend (build clean, 0 warnings/errors) found the
surface ~90% wired endpoint → repository → stored proc → migration, with one
hard blocker and a set of hardcoded dashboard/report fields. This spec closes
both.

---

## Sub-project 1: Platform Admin Bootstrap

### Problem
Nothing in the codebase ever creates a `Users` row with `IsPlatform=1`:
- No migration seeds a platform admin.
- `UserProvisioningRepository` (run on client-create) only makes tenant-scoped
  school admins (`IsPlatform=false`).
- `dbo.User_Create` accepts `@IsPlatform bit` but no caller passes `true`.

Result: on a fresh database **no one can log in as Catre admin**, and the entire
platform surface is unreachable.

### Goal
On a fresh deployment, guarantee exactly one active Catre platform admin exists
so the platform surface is reachable. The admin logs in via the existing **email
OTP** flow (no password seeded; the admin may set one later via
`/v1/auth/set-password`).

### Method (chosen)
**Startup seeder from configuration**, idempotent, runs every boot after
migrations.

### Components
1. **New proc `dbo.PlatformAdmin_Exists`** — returns `1` if any `Users` row has
   `IsPlatform=1 AND Status='active'`, else `0`. Idempotency guard.
2. **`PlatformAdminSeeder`** — startup routine in `Sms.Api`, runs after
   migrations:
   - If `PlatformAdmin_Exists` → log "platform admin present" and no-op.
   - Else → call existing
     `UserProvisioningRepository.CreateUserAsync(tenantId: null, email, phone,
     isPlatform: true, roles: ["platform.only"])`, then log the seeded email.
3. **Config keys** `Catre:AdminEmail` + `Catre:AdminPhone`, read from
   configuration/secrets.
   - **Fail-fast:** if no platform admin exists *and* config is missing/blank,
     throw at startup (matches the existing secrets-fail-fast pattern). Never
     silently start an unreachable platform.

### Data flow
```
startup → migrations → PlatformAdmin_Exists?
   ├─ yes → no-op (log)
   └─ no  → CreateUserAsync(IsPlatform=1) → User_Create + UserRole_Add(platform.only)
admin → POST /v1/auth/otp/request (email) → /v1/auth/otp/verify
      → JWT with is_platform=1 → full Catre surface unlocked
```

### Idempotency & safety
- Runs every boot, no-ops once seeded.
- Seeding keyed on existence-of-any-platform-admin, so redeploys/restarts never
  create duplicates.
- Existence check is identity-agnostic (any active platform admin), avoiding
  ties to a specific environment.

### Testing
- Fresh DB + valid config → startup seeds exactly one platform admin.
- Second startup → no-op (still exactly one).
- OTP login as the seeded admin → JWT has `is_platform=1` and reaches
  `/v1/clients` (200, not 403).
- Empty DB + missing config → startup throws (fail-fast).

### Config for this deployment
- `Catre:AdminEmail = catre.tech@gmail.com`
- `Catre:AdminPhone` = operator-provided.

---

## Sub-project 2: Dashboard + Revenue Real Data

### Problem
`DashboardRepository.OverviewAsync` and `ReportRepository.RevenueAsync` return
hardcoded/empty values for eight fields. Schema review shows these split into
two tiers by data availability.

### Tier A — fully real now (queries only, no schema change)

| Field | Real source |
|-------|-------------|
| **RecentActivity** | `SELECT TOP 20 … FROM AuditLog ORDER BY At DESC` (actor/action/target/kind/at already exist) |
| **RevenueSeries** (by month) | Paid `Invoices` grouped by `month(PaidOn)`, sum `Amount` |
| **SignupSeries** (by month) | `Subscriptions.StartedAt` grouped by month (= new clients/month) |
| **UsageAlerts** | `Tenants.StudentsCount/StorageGb` vs `LimitsStudents/LimitsStorageGb`; alert when usage ≥ **80%** of limit |
| **SystemHealth** | Real DB-connectivity probe (reuse the `/health/ready` check) instead of a hardcoded string |

### Tier B — no historical data exists; forward-filling snapshot

`MrrSeries`, `ChurnPct`, `GrossChurnPct`, and `NetGrowth` cannot be computed
retroactively: there is **no `CreatedAt` on `Tenants`, no cancellation timestamp
on `Subscriptions`, and no MRR history** anywhere. Past months cannot be
invented.

**Solution:** a small snapshot table written idempotently once per month.

- **New table `PlatformMetricsSnapshot`**: `Month (date, PK), Mrr (decimal),
  ActiveClients (int), ChurnedClients (int), CreatedAt`.
- **`MetricsSnapshotWriter`** — startup routine that upserts the **current
  month's** row each boot (so the latest point is always live-accurate) and
  leaves prior months immutable.
- Derivations:
  - `MrrSeries` ← snapshot history, ordered by `Month`.
  - `ChurnPct` = `ChurnedClients / ActiveClients` (previous→current month).
  - `NetGrowth` = `ActiveClients` delta between consecutive snapshots.

**Accepted limitation (explicit):** on day one the series has only the current
month; history accumulates going forward. Churn appears from month 2 onward
(needs a prior snapshot). No past data is fabricated.

### Components
- Update `dbo.Dashboard_CatreOverview` — add result sets for recent activity,
  revenue series, signup series, usage alerts.
- New procs: `dbo.PlatformMetrics_UpsertCurrentMonth`,
  `dbo.PlatformMetrics_GetSeries`.
- Rewrite `DashboardRepository.OverviewAsync` and
  `ReportRepository.RevenueAsync` to map the new result sets (drop hardcoded
  `0`/`[]`).
- New migration: `PlatformMetricsSnapshot` table.
- `MetricsSnapshotWriter` startup routine.

### Testing
- Seed invoices/subscriptions/audit rows → dashboard returns matching real
  values (counts, revenue series points, signup series, recent activity,
  usage alerts firing at ≥80%).
- Snapshot writer upserts current month idempotently (two boots → one row for
  the month, current values).
- Revenue report ARR/ARPA/plan-perf unchanged (already real); `NetGrowth`/churn
  derive from snapshots once ≥2 months exist.

---

## Out of scope (deferred)
- **Real payment gateway** (currently `StubPaymentGateway`) — needs provider
  choice (Stripe/Razorpay/Paddle) + credentials. Separate spec.
- **Real SMS OTP** (currently `ConsoleOtpSender`) — needs provider choice
  (Twilio/MSG91/SNS) + credentials. Email OTP already works. Separate spec.

## Build order
1. Sub-project 1 (bootstrap) — critical path; unblocks everything including
   manual testing of #2.
2. Sub-project 2 (dashboard/revenue real data).
