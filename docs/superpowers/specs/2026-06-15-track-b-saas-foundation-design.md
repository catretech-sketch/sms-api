# SMS Backend — Track B: SaaS Foundation (Design Spec)

> **Status:** Approved design (2026-06-15). Makes the multi-tenant platform genuinely usable as a
> SaaS: a provisioned school can log in, the plan tier gates features, and billing state gates access.
> Builds on the green Phase 0 foundation + Phase 0.5 hardening. **Sibling tracks (separate specs):**
> Track A (finish the ~55 missing per-app endpoints) and Track C (Redis backplane, blob storage,
> real OTP/SMS/email/payment providers, observability). This spec is Track B only.

## Context

An end-to-end audit (2026-06-15) of the implemented backend (32 migrations, ~100 endpoints, 82 tests
green, per-app Swagger) found the platform **hardened but not yet a usable SaaS**:

- **No real login path.** `POST /v1/clients` provisions a tenant with **no user account**, and nothing
  sets a school user's password. No school can authenticate.
- **Feature gating is dead scaffolding.** `ITenantFeatureSet`, `RequiresFeatureAttribute`, and
  `Policies` exist in `Sms.Shared.Kernel/Authz` but have **no implementation, no DI registration, and
  are wired to nothing**. There is no tier→feature mapping.
- **Billing state is inert.** `Tenants.Status` is stored but enforced nowhere; a `past_due` or
  `suspended` tenant has full access.

Track B closes these three gaps and adds in-tenant user invites, turning the foundation into a
sign-inable, plan-aware, billing-aware multi-tenant SaaS.

**Decisions locked during brainstorming (2026-06-15):**
- **First-admin login:** **activation-token flow** (no email dependency). Provisioning creates a
  *pending* admin user and a one-time activation token (logged via the existing `IOtpSender` in dev);
  the admin sets their password via `POST /v1/auth/activate`.
- **Feature gating:** **tier→features code map** (version-controlled). `silver|gold|platinum →
  feature keys`. No schema change. `RequiresFeature("x")` checks the current tenant's set.
- **Billing gate:** `active`/`trial` = full; **`past_due` = read-only** (writes → `402`);
  **`suspended` = blocked** except `/v1/auth/*` and billing endpoints. **Platform (Catre) users exempt.**
- **User management:** a generic **`POST /v1/users` invite** (school admin) reusing the activation
  flow, one user at a time, with role(s).
- **Tenant plan/status is read fresh per request** (not baked into the JWT), so billing/suspension
  takes effect immediately rather than lingering until token expiry.

**Non-goals (explicitly out of this track):**
- No email/SMS/push delivery — activation tokens are returned/logged, not emailed (Track C).
- No bulk CSV user import (Track A — School Admin app).
- **No seat-limit enforcement** — `Subscriptions.Seats` exists; gating on it is a deferred fast-follow.
- No new business endpoints beyond auth/activation, client-provisioning extension, and `POST /v1/users`.

---

## Items

### Item 1 — Activation tokens + pending users (schema + procs)

**Design.**
- New migration **`M0033_SaaS_Foundation_Tables`**: create `ActivationTokens`
  (`Id` PK, `UserId`, `TokenHash varchar(128)`, `ExpiresAt datetime2`, `ConsumedAt datetime2 null`,
  `CreatedAt datetime2`), index on `TokenHash`. Mirrors the `RefreshTokens` pattern.
- Reuse existing columns — **no new columns**: a *pending* user is `Users.Status = 'pending'` with
  `PasswordHash = NULL`; activation sets the hash and flips `Status = 'active'`.
- New migration **`M0034_Procs_Saas`** embeds (CREATE OR ALTER) procs:
  - `User_Create @TenantId, @Email, @IsPlatform` → inserts `Status='pending'`, returns `Id`.
  - `UserRole_Add @UserId, @Role`.
  - `ActivationToken_Insert @UserId, @TokenHash, @ExpiresAt`.
  - `ActivationToken_GetActive @TokenHash` → `UserId` where not consumed and not expired.
  - `ActivationToken_Consume @TokenHash` → sets `ConsumedAt`.
  - `User_Activate @UserId, @PasswordHash` → sets hash + `Status='active'`.
  - `Tenant_GetTierAndStatus @TenantId` → `Tier, Status` (read directly; `Tenants` is **not** RLS-scoped).

**RLS note.** `Users` carries the tenant RLS filter+block predicate. `User_Create` therefore runs under
a session whose `TenantId` equals the new user's `TenantId` (admin invites within their own tenant) or
under a platform session (Catre seeding the first admin) — both satisfy the block predicate.

**Acceptance.** Migrations apply idempotently on the throwaway test DB; a pending user has no usable
password; `ActivationToken_GetActive` returns nothing for consumed/expired tokens.

### Item 2 — Activation endpoint + provisioning + invites

**Design.**
- `AuthRepository` (or a new `UserProvisioningRepository`) gains: `CreatePendingUserAsync(tenantId,
  email, roles, isPlatform)` returning `(userId, rawToken)`; `ActivateAsync(rawToken, passwordHash)`
  returning the activated user or null.
- **`POST /v1/auth/activate`** `{ token, password }` (public, `auth` rate-limit): validates the token
  via `ActivationToken_GetActive`, hashes the password, calls `User_Activate`, consumes the token, then
  **auto-logs-in** (issues access + refresh exactly as `/login` does, with the user's real
  tenant/roles/platform). Invalid/expired/consumed token → `401 invalid_token`.
- **`POST /v1/clients`** (Catre, platform policy) extended: accepts `admin_email`. After creating the
  tenant it creates a pending `school.admin` user + activation token and returns the token in the
  response (dev also logs it via `IOtpSender`). Existing client fields unchanged.
- **`POST /v1/users`** (requires `school.admin`) `{ email, roles[] }`: creates a pending user in the
  **caller's tenant** (from `ITenantContext.TenantId`) with the given roles + activation token; returns
  the token. Rejects empty/unknown roles (roles must be in `Policies.All` minus `platform.only`).

**Acceptance.** Provision a client with `admin_email` → activation token issued → `/auth/activate`
sets the password → `/login` succeeds with the `school.admin` role and correct `tenant_id`. Invite via
`/v1/users` → activate → `/login` carries the assigned role. A non-admin calling `/v1/users` → `403`.

### Item 3 — Tier→feature set + RequiresFeature enforcement

**Design.**
- `TierFeatures` (static, in `Sms.Shared.Kernel/Authz`): a map `tier → string[]` of feature keys. Seed
  set (illustrative, finalised in the plan): `silver` = core (sis, attendance, exams, fees, comms.chat);
  `gold` = silver + `transport.gps`, `exams.datesheet`, `reports.csv`; `platinum` = gold +
  `analytics.advanced`, `comms.announcements.targeted`. Unknown/empty tier → core set only.
  (Platform-only capabilities such as tenant impersonation are RBAC `platform.only`, **not** tier
  features.)
- `TierFeatureSet : ITenantFeatureSet` — `Has(feature)` looks up the **current tenant's tier** (from the
  per-request `ITenantPlan`, Item 4) against `TierFeatures`. Registered scoped in DI.
- `RequiresFeatureFilter : IEndpointFilter` — reads the endpoint's `RequiresFeatureAttribute` metadata
  (existing scaffold) and returns `403 { code: "feature_locked" }` via the standard envelope when
  `ITenantFeatureSet.Has(feature)` is false. Applied through a route-group convention helper so an
  endpoint opts in with `.RequiresFeature("transport.gps")`.

**Acceptance.** A `silver` tenant hitting an endpoint marked `RequiresFeature` for a gold/platinum
feature gets `403 feature_locked`; a `platinum` tenant gets through; platform users bypass.

### Item 4 — Per-request tenant plan + billing-state gate

**Design.**
- `ITenantPlan` (scoped): `{ Guid? TenantId, string Tier, string Status }`, default empty.
- `TenantResolutionMiddleware` (already runs after auth): after populating `ITenantContext`, for a
  tenant (non-platform) caller it loads `Tenant_GetTierAndStatus` **once** and fills `ITenantPlan`. The
  read runs under a platform session (Tenants is not RLS-scoped) and is skipped for platform users and
  anonymous requests.
- `BillingStateMiddleware` (new, immediately after tenant resolution, before authorization):
  - Platform users and `/v1/auth/*` → always pass (so a blocked tenant can still log in and read its
    own suspended/past-due state).
  - `suspended` → `403 { code: "tenant_suspended" }` for everything except `/v1/auth/*`.
  - `past_due` → safe methods (GET/HEAD/OPTIONS) pass; mutating methods → `402 { code:
    "payment_required" }`.
  - `active`/`trial`/unknown → pass.
  - **Status transitions are out of this track's scope** (no dunning automation): a tenant is moved to
    `past_due`/`suspended`/`active` via the **existing** Catre endpoint `POST /v1/clients/{id}/status`
    (platform). Track B only *enforces* the gate; tests set status via that endpoint or seed it directly.
    A school-facing self-serve payment endpoint is deferred (Track A/C), so the gate exempts
    `/v1/auth/*` only.
- **Middleware order** becomes: `UseExceptionHandler → Serilog → UseCors → UseRateLimiter →
  UseAuthentication → TenantResolution(+plan) → BillingStateGate → UseAuthorization → endpoints`.

**Acceptance.** `past_due` tenant: GET `200`, POST `402`; billing endpoints still reachable.
`suspended` tenant: all `403` except `/v1/auth/*` and billing. Platform user: unaffected in every state.

---

## Architecture impact

- **Schema:** one new table (`ActivationTokens`); no other tables; no column changes (statuses are
  existing string columns). New procs in `M0034`.
- **`Sms.Shared.Kernel`:** `TierFeatures`, `TierFeatureSet`, `ITenantPlan`/`TenantPlan`,
  `RequiresFeatureFilter`, `BillingStateMiddleware`; `TenantResolutionMiddleware` extended to load plan.
- **`Sms.Api`:** `POST /v1/auth/activate`; `POST /v1/clients` extended with `admin_email`;
  `POST /v1/users`; new middleware + feature-filter wiring in `Program.cs`; DI registrations.
- **Per-app Swagger:** `/v1/auth/activate` → all apps (auth resource); `/v1/users` → school-admin.
  Add to `ApiAudienceMap`.
- **No change** to existing business endpoints' surface; they gain billing/feature gating transparently
  via middleware/filters.

## Testing

All TDD (failing test → implement → green), matching existing discipline.

**Unit (`Sms.Tests.Unit`):**
- `TierFeatures`/`TierFeatureSet`: silver lacks a gold feature; platinum has all; unknown tier → core.
- `RequiresFeatureFilter`: returns `403 feature_locked` when the feature is absent, passes otherwise.
- `BillingStateMiddleware` logic: `past_due` blocks mutations / allows reads; `suspended` blocks all but
  auth+billing; platform bypass; method classification correct.

**Integration (`Sms.Tests.Integration`, real SQL, throwaway DB):**
- Provision client with `admin_email` → token → `/auth/activate` → `/login` works as `school.admin`
  with correct `tenant_id`.
- `/v1/users` invite → activate → `/login` carries assigned role; non-admin → `403`.
- Feature gate end-to-end: silver tenant → `403 feature_locked`; platinum tenant → `200` on a
  `RequiresFeature`-marked endpoint.
- Billing gate end-to-end: `past_due` GET `200` / POST `402`; `suspended` `403` except auth+billing;
  platform user unaffected.

## Definition of done

Build clean (warnings-as-errors); all existing + new unit and integration tests green against
`DESKTOP-TJL4SG6`; a freshly provisioned school can activate and log in; tier gating returns
`feature_locked` for locked features; billing gate enforces read-only (`past_due`) and block
(`suspended`) with platform exemption; `POST /v1/users` invites work. **Seat enforcement, email/SMS
delivery, and bulk import remain out of scope** (Tracks B-fast-follow / C / A respectively).
