# SMS Backend — Track B: SaaS Foundation (Design Spec)

> **Status:** Approved design (2026-06-15, rev. 2). Makes the multi-tenant platform genuinely usable as
> a SaaS: provisioned/imported people can log in (password **or** OTP), the plan tier gates features,
> and billing state gates access. Builds on the green Phase 0 foundation + Phase 0.5 hardening.
> **Sibling tracks (separate specs):** Track A (finish the ~55 missing per-app endpoints) and Track C
> (real SMS/email/push + payment providers, Redis backplane, blob storage, observability).

## Context

An end-to-end audit (2026-06-15) of the implemented backend (32 migrations, ~100 endpoints, 82 tests
green, per-app Swagger) found the platform **hardened but not yet a usable SaaS**:

- **No usable login path.** `POST /v1/clients` provisions a tenant with **no user account**; the only
  login is email+password, but nothing sets a school user's password, and there is no OTP login despite
  the staff/student apps expecting it. OTP scaffolding exists but is wired to nothing.
- **Feature gating is dead scaffolding.** `ITenantFeatureSet`, `RequiresFeatureAttribute`, and
  `Policies` exist in `Sms.Shared.Kernel/Authz` with **no implementation, no DI, wired to nothing**.
- **Billing state is inert.** `Tenants.Status` is stored but enforced nowhere.
- **No way to load people in bulk** — every user would have to be created one by one.

Track B closes these gaps: dual login (password **and** OTP), tier→feature gating (wires the dead
scaffold), a per-request billing-state gate, single + bulk user provisioning.

**Existing groundwork to build on (not rebuild):** `OtpCodes` table + `Otp_Insert`/`Otp_GetActive`
procs + `IOtpSender`/`ConsoleOtpSender` (all present, unused); `Users` already has `Email`, `Phone`,
`StudentId`, nullable `PasswordHash`, `Status`; `User_GetByEmail`/`User_GetByStudentId` procs;
`Subscriptions.Seats`; `POST /v1/clients/{id}/status` lifecycle endpoint.

**Decisions locked during brainstorming (2026-06-15):**
- **Login = password OR OTP.** Keep the existing email+password login. **Add OTP login** (email *or*
  mobile): a user whose email/phone is **already in the DB** requests a one-time code and logs in.
  OTP is the passwordless first-login for provisioned/imported users; any user may additionally **set a
  password** (authenticated) to enable password login. **No activation-token flow** — OTP covers it.
- **OTP delivery** is via the existing `IOtpSender` (console/log in dev). Real SMS/email is **Track C**;
  the OTP mechanism (generate, store hashed, verify) is fully built now.
- **Feature gating:** **tier→features code map** (version-controlled), mechanism fully wired. **All
  tiers currently grant all features** ("all level") — nothing is locked yet; the map is the single
  place to tighten a tier later with no endpoint changes. No schema change.
- **Billing gate:** `active`/`trial` = full; **`past_due` = read-only** (writes → `402`);
  **`suspended` = blocked** except `/v1/auth/*`. **Platform (Catre) users exempt.** Status transitions
  use the existing `POST /v1/clients/{id}/status`.
- **User provisioning:** `admin_email` on client creation seeds the school admin; **`POST /v1/users`**
  invites one user; **`POST /v1/users/import`** bulk-loads teachers/students/staff. All create
  **login-capable Users + roles only** (domain profiles are Track A); imported users log in via OTP.
- **Tenant plan/status is read fresh per request** (not baked into the JWT), so billing/suspension
  takes effect immediately.

**Non-goals (out of this track):** real SMS/email/push delivery (Track C); domain-record enrichment in
import (Student/Teacher/Staff profile tables — Track A); seat-limit *enforcement* (deferred fast-follow,
`Subscriptions.Seats` already exists); no other new business endpoints.

---

## Items

### Item 1 — OTP login mechanism (email + mobile) over known users

**Design.**
- New migration **`M0033_Saas_Auth`** generalises `OtpCodes` for email *or* phone: add
  `Identifier varchar(256)` (the email/phone the code was issued to) and `Channel varchar(10)`
  (`sms`|`email`); make the legacy `Phone` column nullable. Index `Identifier`.
- Procs (CREATE OR ALTER in **`M0034_Procs_Saas`**):
  - `User_GetByPhone @Phone` → same shape as `User_GetByEmail` (new; mirrors existing lookup).
  - `Otp_Insert @Identifier, @Channel, @CodeHash, @ExpiresAt` (replaces the phone-keyed version).
  - `Otp_GetActive @Identifier, @CodeHash` → row when not consumed and not expired.
  - `Otp_Consume @Identifier, @CodeHash` → sets `ConsumedAt`.
- `IOtpSender` generalised to `SendAsync(string identifier, string channel, string code)`;
  `ConsoleOtpSender` logs it. (Real providers = Track C.)

**Acceptance.** Migrations apply idempotently; `Otp_GetActive` returns nothing for consumed/expired
codes; a code issued to an email validates only for that email.

### Item 2 — Auth endpoints: OTP request/verify + set-password

**Design.** `AuthRepository` gains `GetByPhoneAsync`, `SetPasswordAsync(userId, hash)`, and OTP helpers
(`IssueOtpAsync(identifier, channel)`, `VerifyOtpAsync(identifier, code)`).
- **`POST /v1/auth/otp/request`** `{ identifier }` (public, `auth` rate-limit): classify `identifier`
  as email or phone; look up the user (`GetByEmail`/`GetByPhone`). **Always returns `200`** (never
  leaks whether the account exists). If found, generate a **6-digit** code, store its hash via
  `Otp_Insert` with a **10-minute TTL**, and `IOtpSender.SendAsync`.
- **`POST /v1/auth/otp/verify`** `{ identifier, code }` (public, `auth` rate-limit): `Otp_GetActive` →
  on match, `Otp_Consume` and **issue access + refresh** with the user's real tenant/roles/platform
  (same token path as `/login`). Invalid/expired → `401 invalid_code`.
- **`POST /v1/auth/set-password`** `{ password }` (**authenticated**): hashes and stores via
  `SetPasswordAsync` so the caller can thereafter use email+password login. Existing `/login` unchanged.

**Acceptance.** A user with an email in the DB completes request→verify and receives valid tokens with
correct claims; a non-existent identifier still returns `200` on request and `401` on verify; after
`set-password`, `/login` works for that user.

### Item 3 — User provisioning: admin seed, single invite, bulk import

**Design.** Procs in `M0034`: `User_Create @TenantId, @Email, @Phone, @IsPlatform` → inserts
`Status='active'` with `PasswordHash NULL` (login-ready via OTP), returns `Id`; `UserRole_Add
@UserId, @Role`; `Users_BulkCreate @TenantId, @Rows` (**table-valued parameter**) for bulk insert of
`(Email, Phone, Role)` rows in one round-trip (the established TVP pattern).
- **`POST /v1/clients`** (Catre) extended with `admin_email`: after creating the tenant, create a
  `school.admin` user in it (the admin then OTP-logs-in to that email). Existing fields unchanged.
- **`POST /v1/users`** (requires `school.admin`) `{ email?, phone?, roles[] }`: create one login user in
  the caller's tenant with the given roles (must be in `Policies.All` minus `platform.only`; at least
  one of email/phone required).
- **`POST /v1/users/import`** (requires `school.admin`) `{ rows: [{ email?, phone?, role }] }`: validate
  rows, bulk-insert via the TVP proc, return a summary `{ created, skipped, errors[] }`. Rows are
  login users + roles only (teacher/student/staff per `role`); **domain profiles are Track A.**
  CSV/XLSX parsing happens in the frontend, which posts JSON rows.

**Acceptance.** Provision client with `admin_email` → that email completes OTP login as `school.admin`
with the right `tenant_id`. `/v1/users` invite → invitee OTP-logs-in with assigned role; non-admin →
`403`. `/v1/users/import` with N valid rows creates N login users (idempotent on duplicate
email/phone → counted in `skipped`), each able to OTP-login.

### Item 4 — Tier→feature set + RequiresFeature enforcement

**Design.**
- `FeatureCatalog` (static, `Sms.Shared.Kernel/Authz`): the full set of known feature keys (e.g.
  `transport.gps`, `exams.datesheet`, `reports.csv`, `analytics.advanced`, `comms.announcements.targeted`).
- `TierFeatures` (static): map `tier → string[]`. **Decision: all tiers (`silver`/`gold`/`platinum`,
  and unknown/empty) currently grant the FULL `FeatureCatalog`** — no feature is locked in this track.
  This single map is the one place to tighten a tier later (remove keys), with **zero endpoint changes**.
  (Platform-only capabilities are RBAC `platform.only`, **not** tier features.)
- `TierFeatureSet : ITenantFeatureSet` — `Has(feature)` resolves the current tenant's tier (from the
  per-request `ITenantPlan`, Item 5) against `TierFeatures`. Scoped DI registration.
- `RequiresFeatureFilter : IEndpointFilter` — reads the endpoint's `RequiresFeatureAttribute` metadata
  (existing scaffold) → `403 { code: "feature_locked" }` via the standard envelope when the current
  tenant's set lacks it. Opt-in per endpoint via a `.RequiresFeature("transport.gps")` route-group
  helper. Platform users bypass. The mechanism is fully wired and tested even though no tier locks a
  feature today — so tightening a tier later is a one-line map edit, already enforced.

**Acceptance.** The filter returns `403 feature_locked` whenever the current feature set lacks the
required key (proven with a feature set that omits it); with the shipped all-tiers map, a
`RequiresFeature`-marked endpoint returns `200` for every tier; platform users bypass.

### Item 5 — Per-request tenant plan + billing-state gate

**Design.**
- `ITenantPlan` (scoped): `{ Guid? TenantId, string Tier, string Status }`, default empty.
- `TenantResolutionMiddleware` (already runs after auth): for a non-platform caller, load
  `Tenant_GetTierAndStatus @TenantId` **once** and fill `ITenantPlan`. Read runs under a platform
  session (`Tenants` is not RLS-scoped); skipped for platform/anonymous.
- `BillingStateMiddleware` (new, after tenant resolution, before authorization):
  - Platform users and `/v1/auth/*` → always pass (a blocked tenant can still log in / read its state).
  - `suspended` → `403 { code: "tenant_suspended" }` except `/v1/auth/*`.
  - `past_due` → safe methods (GET/HEAD/OPTIONS) pass; mutating methods → `402 { code:
    "payment_required" }`.
  - `active`/`trial`/unknown → pass.
  - Status transitions are out of scope (no dunning automation): use the existing
    `POST /v1/clients/{id}/status` (platform). Tests set status via that endpoint or seed it.
- **Middleware order:** `UseExceptionHandler → Serilog → UseCors → UseRateLimiter → UseAuthentication →
  TenantResolution(+plan) → BillingStateGate → UseAuthorization → endpoints`.

**Acceptance.** `past_due`: GET `200`, POST `402`. `suspended`: all `403` except `/v1/auth/*`. Platform
user unaffected in every state.

---

## Architecture impact

- **Schema:** generalise `OtpCodes` (`Identifier`, `Channel`; `Phone` nullable); **no new tables, no new
  columns elsewhere** (statuses are existing string columns; passwordless users are created `active`
  with the existing nullable `PasswordHash` left `NULL` and log in via OTP). New procs in `M0034`
  (incl. a `Users` TVP type for bulk import).
- **`Sms.Shared.Kernel`:** generalised `IOtpSender`; `AuthRepository` gains phone lookup, OTP helpers,
  set-password; `TierFeatures`, `TierFeatureSet`, `ITenantPlan`/`TenantPlan`, `RequiresFeatureFilter`,
  `BillingStateMiddleware`; `TenantResolutionMiddleware` loads plan.
- **`Sms.Api`:** `POST /v1/auth/otp/request`, `/otp/verify`, `/set-password`; `admin_email` on
  `POST /v1/clients`; `POST /v1/users`, `POST /v1/users/import`; middleware + feature-filter wiring + DI.
- **Per-app Swagger (`ApiAudienceMap`):** `/v1/auth/*` (incl. new OTP) → all apps; `/v1/users*` →
  school-admin.
- Existing business endpoints gain billing/feature gating transparently; their surface is unchanged.

## Testing

All TDD (failing test → implement → green).

**Unit (`Sms.Tests.Unit`):**
- Identifier classification (email vs phone); OTP code generation/format.
- `TierFeatures`/`TierFeatureSet`: every tier grants the full `FeatureCatalog`; `Has(unknownKey)` false.
- `RequiresFeatureFilter`: `403 feature_locked` when the set omits the key (stub set), pass otherwise.
- `BillingStateMiddleware`: `past_due` blocks mutations / allows reads; `suspended` blocks all but
  `/v1/auth/*`; platform bypass; method classification.

**Integration (`Sms.Tests.Integration`, real SQL, throwaway DB):**
- OTP login: seed a user with an email → request (non-leaking `200`) → verify → tokens with correct
  claims; wrong/expired code → `401`. Phone path likewise.
- `set-password` then email+password `/login` succeeds.
- Provision client with `admin_email` → OTP login as `school.admin`, correct `tenant_id`.
- `/v1/users` invite → OTP login with assigned role; non-admin → `403`.
- `/v1/users/import` N rows → N OTP-capable users; duplicates → `skipped`.
- Feature gate: a `RequiresFeature`-marked endpoint returns `200` for a normal tenant (all tiers grant
  all features today); the `403 feature_locked` path is proven at unit level with a set omitting the key.
- Billing gate: `past_due` GET `200` / POST `402`; `suspended` `403` except `/v1/auth/*`; platform
  unaffected.

## Definition of done

Build clean (warnings-as-errors); all existing + new unit and integration tests green against
`DESKTOP-TJL4SG6`; a provisioned school admin can OTP-log-in; password and OTP login both work; a user
can set a password; single + bulk user import create OTP-capable users with roles; the feature-gating
mechanism is wired and tested (all tiers grant all features today; `feature_locked` enforced whenever a
set omits a key); billing gate enforces read-only (`past_due`) and block
(`suspended`) with platform exemption. **Real SMS/email delivery, domain-profile import, and seat
enforcement remain out of scope** (Track C / Track A / deferred fast-follow).
