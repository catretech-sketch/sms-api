# Catre Super-Admin API (`sms-catreadmin`) — Contract Reference

> **Audience:** the frontend engineer/agent wiring `sms-catreadmin` to the live backend.
> **Status:** **Forward contract.** These endpoints are the Phase-1 deliverable — the backend
> currently implements only Phase 0 (auth + health). Build the frontend against this contract; the
> backend will be built to match it. Source of truth: `sms-catreadmin/api/contracts.js` +
> `sms-backend/docs/2026-06-13-backend-api-design.md` §3B/§3C + the Phase-1 roadmap.
> Machine-readable twin: [`catreadmin-api.openapi.yaml`](./catreadmin-api.openapi.yaml).

---

## 1. Conventions (apply to every endpoint)

| Concern | Rule |
|---|---|
| Base URL | `{API_BASE_URL}` + `/v1` (e.g. `https://api.catre.io/v1`) |
| Casing | **snake_case** for every JSON key, request and response |
| Dates | **ISO-8601 UTC** — date-only fields are `YYYY-MM-DD`, timestamps are `YYYY-MM-DDThh:mm:ssZ` |
| Money | INR, `decimal(18,2)` (whole-rupee values in current data, e.g. `mrr: 14999`). Never floats client-side for math — treat as decimal strings/numbers as given |
| Success body | single: `{ "data": { ... } }` · list: `{ "data": [ ... ], "next_cursor": "..."\|null }` |
| Error body | `{ "error": { "code": "snake_code", "message": "human text", "details": { "field": ["..."] }\|null } }` |
| Auth | `Authorization: Bearer <access_token>` on every endpoint except `/auth/login` and `/auth/refresh` |
| Tenant scope | Catre staff are **platform** users (RLS-bypass). `X-Tenant-Id` is **only** sent while impersonating a school (see §3.10) |
| Paging | cursor-based: `?limit=50&cursor=<opaque>`; response returns `next_cursor` (null = last page). `limit` 1–200, default 50 |
| Validation errors | HTTP **422** with `error.code = "validation_error"` and per-field `details` |
| Content type | `application/json` (except CSV export, §3.13) |

### Standard error codes
`invalid_credentials` (401) · `invalid_token` (401) · `unauthorized` (401) · `forbidden` (403, RBAC/tier) · `not_found` (404) · `validation_error` (422) · `conflict` (409, e.g. slug taken) · `rate_limited` (429) · `internal_error` (500).

---

## 2. Authentication & roles

Auth surface is unified across all SMS apps (§3C). For Catre, log in with email + password.

### Roles (Catre team)
`owner` · `admin` · `support` · `sales` · `finance` · `analyst`. Issued as `role` claims in the access
token; `is_platform = 1` for all Catre staff. The RBAC matrix (§4) defines which role may call which
mutation; the server enforces it (403 `forbidden` on violation).

### `POST /v1/auth/login`
Request:
```json
{ "email": "rohan@catre.io", "password": "••••••••" }
```
`200`:
```json
{ "data": { "access_token": "eyJhbGc…", "refresh_token": "b64-opaque" } }
```
Errors: `422` (missing email/password), `401 invalid_credentials`.

### `POST /v1/auth/refresh`
Request: `{ "refresh_token": "b64-opaque" }` → `200` same shape as login (rotates the refresh token; old one is revoked). `401 invalid_token` if expired/revoked.

### `GET /v1/auth/me`
`200`:
```json
{ "data": { "id": "guid", "tenant_id": null, "roles": ["owner"] } }
```
(`tenant_id` is `null` for platform users.)

### `POST /v1/auth/logout`
Request: `{ "refresh_token": "b64-opaque" }` → `204` (revokes the token).

---

## 3. Resources

> Field tables below list every key the response exposes. **Enums** show the exact allowed string values.

### 3.1 Client (Tenant)

The school/tenant. Catre's primary entity. Response keys (from `contracts.js TENANT_KEYS`, canonical superset in §3B):

| Field | Type | Notes / enum |
|---|---|---|
| `id` | string (guid) | |
| `name` | string | |
| `slug` | string | URL-safe, unique |
| `country` | string | "City, State" e.g. `"Mumbai, MH"` |
| `status` | enum | `trial` · `active` · `past_due` · `suspended` · `cancelled` |
| `plan_id` | string (guid) | current plan |
| `plan_name` | string | denormalized for display |
| `tier` | enum | `trial` · `silver` · `gold` · `platinum` · `metered` · `exclusive` |
| `mrr` | decimal | monthly recurring revenue (INR) |
| `students_count` | int | |
| `staff_count` | int | |
| `storage_gb` | decimal | storage used |
| `limits` | object | `{ "students": int, "staff": int, "storage_gb": int }` |
| `created` | date | `YYYY-MM-DD` |
| `last_active_days` | int | days since last activity (0 = today) |
| `trial_ends_days` | int \| null | days until trial ends; negative = overdue; null = not trialing |
| `contact` | object | `{ "name": string, "email": string, "phone": string }` |
| `csm` | string | customer-success manager name |
| `health_score` | int | 0–100 |
| `gateway` | object | payment mandate, see 3.1.1 |
| `usage_series` | int[] | last 14 days active-student counts |

Canonical superset fields the backend may also return (optional, §3B): `city`, `currency`, `timezone`, `logo_url`, `color`.

#### 3.1.1 Gateway (nested mandate)
| Field | Type | Enum |
|---|---|---|
| `provider` | enum | `Razorpay` · `PayU` · `Cashfree` |
| `method` | enum | `upi_autopay` · `enach` · `card` · `none` |
| `mandate` | enum | `active` · `pending` · `paused` · `cancelled` · `none` |
| `vpa` | string \| null | UPI VPA e.g. `greenwood@okhdfcbank` |
| `card` | string \| null | masked, e.g. `HDFC ···· 1234` |
| `bank` | string \| null | for e-NACH |
| `max_amount` | decimal | per-cycle cap |
| `mandate_id` | string | |

**`GET /v1/clients`** — list. Query: `status`, `tier`, `q` (name/slug search), `sort` (`mrr`|`name`|`status`|`plan_name`|`created`|`last_active_days`, prefix `-` for desc; default `-mrr`), `limit`, `cursor`.
`200`: `{ "data": [Client, …], "next_cursor": "…"|null }`.

**`POST /v1/clients`** — create / onboard a school. RBAC: `clients.start_trial` (owner/admin/sales).
Request:
```json
{
  "name": "Greenwood High",
  "slug": "greenwood-high",
  "country": "Mumbai, MH",
  "admin_name": "Priya Sharma",
  "admin_email": "admin@greenwood.edu.in",
  "admin_phone": "+91 90000 00000",
  "plan_id": "pl_gold",
  "trial_days": 14
}
```
`201`: `{ "data": Client }` (status `trial`, invite email sent to `admin_email`). `409 conflict` if slug taken. `422` on validation.

**`GET /v1/clients/{id}`** → `{ "data": Client }` · `404 not_found`.

**`PATCH /v1/clients/{id}`** — update profile/branding/contact/gateway. RBAC: `clients.change_plan`/owner-admin. Body: any subset of mutable Client fields (`name`, `country`, `csm`, `contact`, `color`, `logo_url`, `gateway`). → `{ "data": Client }`.

**`GET /v1/clients/{id}/usage`** — usage detail. → `{ "data": { "students_count": int, "staff_count": int, "storage_gb": decimal, "limits": {…}, "usage_series": int[], "usage_pct": int } }`.

**`GET /v1/clients/{id}/activity`** — audit entries for this tenant (paged list of AuditLog, §3.8).

**`POST /v1/clients/{id}/status`** — lifecycle transition. RBAC varies per target (§4).
Request: `{ "status": "suspended", "reason": "non-payment" }` where `status` ∈ `trial`·`active`·`suspended`·`cancelled` (use `active` to reinstate/activate). → `{ "data": Client }`. `403 forbidden` if role lacks the action; `409 conflict` on illegal transition.

**`POST /v1/clients/{id}/change-plan`** — RBAC `clients.change_plan`. Request `{ "plan_id": "pl_platinum" }` → `{ "data": Client }` (recomputes `tier`, `plan_name`, `limits`, `mrr`).

**`DELETE /v1/clients/{id}`** — RBAC `clients.delete` (**owner only**). Header/body confirm token required: `{ "confirm": "DELETE" }`. → `204`. 90-day retention applies server-side.

**`POST /v1/clients/{id}/impersonate`** — RBAC `clients.impersonate` (owner/admin/support). Returns a **read-only, tenant-scoped** access token; the act is audited and visible to the school.
`200`: `{ "data": { "access_token": "…", "tenant_id": "guid", "read_only": true, "expires_in": 900 } }`. Use it with `Authorization: Bearer` **and** `X-Tenant-Id: <tenant_id>` against school endpoints.

---

### 3.2 Plan
Keys from `contracts.js PLAN_KEYS` + §3B:

| Field | Type | Enum |
|---|---|---|
| `id` | string (guid) | |
| `name` | string | |
| `tier` | enum | `trial`·`silver`·`gold`·`platinum`·`metered`·`exclusive` |
| `pricing` | enum | `flat` · `per_student` |
| `price` | decimal | monthly price (0 for trials) |
| `per_student` | decimal \| null | metered ₹/student/month |
| `min_students` | int \| null | metered floor |
| `period` | enum | `month` · `14 days` · `year` |
| `features` | string[] | feature catalog codes (§5) |
| `limits` | object | `{ "students": int, "staff": int, "storage_gb": int }` |
| `visibility` | enum | `published` · `draft` |
| `audience` | enum | `all` · `new` · `exclusive` |
| `band` | string | size band label, e.g. `"300–1,200 students"` |
| `offer` | object \| null | `{ "label": string, "pct": number }` |
| `color` | string | CSS color token |
| `description` | string | |

**`GET /v1/plans`** (list; `?visibility=`, `?audience=`) · **`GET /v1/plans/{id}`** · **`POST /v1/plans`** (RBAC `plans.manage` owner/admin/finance) · **`PATCH /v1/plans/{id}`** · **`POST /v1/plans/{id}/publish`** body `{ "visibility": "published" }`. All return `{ "data": Plan }` (list returns array).

---

### 3.3 Subscription (§3B)
| Field | Type | Enum |
|---|---|---|
| `id` | string (guid) | |
| `tenant_id` | string (guid) | |
| `plan_id` | string (guid) | |
| `status` | enum | `active` · `trial` · `suspended` · `cancelled` |
| `started_at` | timestamp | |
| `renews_at` | timestamp \| null | next charge / period end |
| `seats` | int | |

**`GET /v1/subscriptions`** (`?tenant_id=`, `?status=`) · **`GET /v1/subscriptions/{id}`** · **`POST /v1/subscriptions`** (`{ tenant_id, plan_id, seats }`). The Billing→Subscriptions tab reads `next charge` from `renews_at` and amount from the plan.

---

### 3.4 Invoice
Keys from `contracts.js INVOICE_KEYS`:

| Field | Type | Enum |
|---|---|---|
| `id` | string | e.g. `INV-10465` |
| `tenant_id` | string (guid) | |
| `tenant_name` | string | denormalized |
| `plan_name` | string | |
| `amount` | decimal | INR |
| `status` | enum | `paid` · `open` · `past_due` |
| `issued` | date | |
| `due` | date | |
| `paid_on` | date \| null | |

**`GET /v1/invoices`** (`?status=`, `?tenant_id=`, paged) · **`GET /v1/invoices/{id}`** ·
**`POST /v1/invoices/{id}/mark-paid`** (RBAC `billing.manage_invoice`) → sets `status=paid`, `paid_on=today` ·
**`POST /v1/invoices/{id}/refund`** (RBAC `billing.refund` owner/finance; invoice must be `paid`) → `status=open`. Returns `{ "data": Invoice }`.

---

### 3.5 Onboarding pipeline (§3B `OnboardingItem`)
| Field | Type | Notes |
|---|---|---|
| `id` | string (guid) | |
| `tenant_id` | string (guid) \| null | null for raw leads |
| `name` | string | school name |
| `slug` | string | |
| `owner` | string | assigned team member |
| `value` | decimal | monthly MRR value |
| `stage` | enum | `lead` · `trial` · `onboarding` · `active` |
| `checklist` | object[] | 5 items: `{ "label": string, "done": bool }`; labels = `Account created`, `Admin invited`, `Data imported`, `First login`, `Payment set up` |
| `done` | int | completed checklist count (0–5) |
| `age` | int | days in pipeline |

**`GET /v1/onboarding`** — pipeline (optionally `?stage=`). Returns flat list with `stage`; the board groups by `stage`.
**`POST /v1/onboarding/{id}/advance`** — RBAC `onboarding.manage`. Body `{ "stage": "trial" }` (drag between columns).
**`PATCH /v1/onboarding/{id}/checklist`** — Body `{ "label": "Admin invited", "done": true }`. Returns `{ "data": OnboardingItem }`.

---

### 3.6 Support ticket (§3B, `contracts.js SUPPORT_TICKET_KEYS`)
| Field | Type | Enum |
|---|---|---|
| `id` | string | e.g. `TK-2040` |
| `subject` | string | |
| `tenant_id` | string (guid) | |
| `tenant_name` | string | |
| `status` | enum | `open` · `pending` · `resolved` · `closed` |
| `priority` | enum | `urgent` · `high` · `normal` · `low` |
| `assignee` | string \| null | team member name |
| `created` | date | |
| `updated` | timestamp | last activity |
| `messages_count` | int | |

#### TicketMessage (in detail view)
| Field | Type | Enum |
|---|---|---|
| `id` | string (guid) | |
| `ticket_id` | string | |
| `who` | string | author name |
| `role` | enum | `client` · `agent` |
| `text` | string | |
| `when` | timestamp | |

**`GET /v1/tickets`** (`?status=`, `?q=`, paged) · **`GET /v1/tickets/{id}`** → `{ "data": { …ticket, "messages": [TicketMessage, …] } }` ·
**`PATCH /v1/tickets/{id}`** (RBAC `support.manage`) body subset of `{ "status", "assignee" }` ·
**`POST /v1/tickets/{id}/messages`** body `{ "text": "…" }` → appends an `agent` message, bumps `updated`/`messages_count`.

---

### 3.7 Team member (§3B, `contracts.js TEAM_MEMBER_KEYS`)
| Field | Type | Enum |
|---|---|---|
| `id` | string (guid) | |
| `name` | string | |
| `email` | string | |
| `role` | enum | `owner` · `admin` · `support` · `sales` · `finance` · `analyst` |
| `status` | enum | `active` · `invited` · `deactivated` |
| `last_login` | timestamp \| null | |
| `joined` | date | |

**`GET /v1/team`** (RBAC `team.view`, owner) · **`POST /v1/team`** (invite: `{ name, email, role }` → status `invited`) · **`PATCH /v1/team/{id}`** (body subset of `{ "role", "status" }`; set `status:"deactivated"`/`"active"` to deactivate/reactivate). All RBAC `team.manage` (owner only).

---

### 3.8 Audit log (§3B)
| Field | Type | Enum |
|---|---|---|
| `id` | string (guid) | |
| `actor_id` | string (guid) | |
| `actor_name` | string | |
| `role` | string | actor role at the time |
| `action` | string | human description |
| `target` | string | affected entity name |
| `kind` | enum | `suspend`·`refund`·`trial`·`impersonate`·`plan`·`team`·`invoice`·`activate` |
| `time` | timestamp | |

**`GET /v1/audit`** — paged list. Filters: `?kind=`, `?actor_id=`, `?tenant_id=`. Append-only; no write endpoint (entries are created server-side by mutations). The dashboard "Recent activity" reads the latest 6.

---

### 3.9 Dashboard
**`GET /v1/dashboard/overview`** — one round-trip (backed by a `QueryMultiple` proc). RBAC `dashboard.view` (all roles).
`200`:
```json
{ "data": {
  "counts": { "total": 0, "active": 0, "trial": 0, "suspended": 0, "cancelled": 0 },
  "mrr": 0,
  "trials_ending": 0,
  "churn_pct": 1.8,
  "months": ["Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar","Apr","May","Jun"],
  "mrr_series": [0,0,0,0,0,0,0,0,0,0,0,0],
  "signup_series": [0,0,0,0,0,0,0,0,0,0,0,0],
  "plan_mix": [ { "label": "Gold", "value": 0, "color": "#…" } ],
  "usage_alerts": [ { "tenant_id": "guid", "name": "…", "usage_pct": 92, "status": "active", "csm": "…" } ],
  "system_health": [ { "name": "API", "status": "operational", "latency": "42ms", "uptime": "99.98%" } ],
  "recent_activity": [ /* latest 6 AuditLog */ ]
} }
```
`usage_alerts` = tenants with `usage_pct >= 80` and `status != cancelled`. `system_health[].status` ∈ `operational`·`degraded`·`down`.

### 3.10 Reports
**`GET /v1/reports/revenue`** — RBAC `reports.view`.
```json
{ "data": {
  "arr": 0, "net_growth": 12, "gross_churn_pct": 1.8, "arpa": 0,
  "months": ["Jul", "…"],
  "revenue_series": [0, "…"],
  "revenue_by_plan": [ { "label": "Gold", "value": 0, "color": "#…" } ],
  "plan_performance": [ { "plan_name": "Gold", "clients": 0, "mrr": 0, "share_pct": 0 } ]
} }
```

### 3.11 Settings
**`GET /v1/settings`** / **`PATCH /v1/settings`** (RBAC `settings.manage`, owner). Body/response:
```json
{ "data": {
  "branding": { "name": "Catre", "accent": "#5b8cff" },
  "announcement": { "on": false, "msg": "" },
  "feature_flags": { "new_gradebook": false, "ai_insights": false, "parent_app_v2": false, "self_serve_billing": false }
} }
```

### 3.12 System health
Exposed inside `dashboard.overview.system_health` (above). No separate public endpoint required for the UI.

### 3.13 CSV export
**`GET /v1/reports/clients.csv`** — RBAC `reports.view`. Returns `text/csv` (not the JSON envelope), `Content-Disposition: attachment; filename="catre-clients.csv"`. Columns: `client, status, plan, mrr, students, staff, country, created`.

---

## 4. RBAC matrix (server-enforced)

Mutations check the caller's role; violations return `403 forbidden`. `view` actions are open to all six roles.

| Permission | Endpoints | owner | admin | support | sales | finance | analyst |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|
| `clients.start_trial` | POST /clients | ✓ | ✓ | | ✓ | | |
| `clients.activate` | POST /clients/{id}/status→active | ✓ | ✓ | | | ✓ | |
| `clients.suspend` | POST …/status→suspended | ✓ | ✓ | | | | |
| `clients.reinstate` | POST …/status→active | ✓ | ✓ | | | ✓ | |
| `clients.cancel` | POST …/status→cancelled | ✓ | ✓ | | | | |
| `clients.change_plan` | POST …/change-plan | ✓ | ✓ | | ✓ | ✓ | |
| `clients.delete` | DELETE /clients/{id} | ✓ | | | | | |
| `clients.impersonate` | POST …/impersonate | ✓ | ✓ | ✓ | | | |
| `plans.manage` | POST/PATCH /plans, publish | ✓ | ✓ | | | ✓ | |
| `billing.manage_invoice` | POST /invoices/{id}/mark-paid | ✓ | ✓ | | | ✓ | |
| `billing.refund` | POST /invoices/{id}/refund | ✓ | | | | ✓ | |
| `onboarding.manage` | advance / checklist | ✓ | ✓ | | ✓ | |  |
| `support.manage` | PATCH /tickets, POST messages | ✓ | ✓ | ✓ | | | |
| `team.manage` | POST/PATCH /team | ✓ | | | | | |
| `settings.manage` | PATCH /settings | ✓ | | | | | |
| `reports.view` | reports, CSV | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

---

## 5. Feature catalog (Plan `features[]` codes)

`sis.students` · `sis.teachers` · `sis.staff` · `sis.parents` · `academics` · `attendance` ·
`attendance.geo` · `exams` · `fees` · `fees.online` · `communication` · `hr.payroll` ·
`ops.library` · `ops.transport` · `ops.hostel` · `ops.sports` · `transport.gps` · `reports` ·
`reports.advanced` · `identity` · `support.dedicated`.

These map to backend tier-gating (`RequireFeature`); a school's active plan determines which modules its users can reach.

---

## 6. Frontend wiring notes

- The frontend already pins these keys in `sms-catreadmin/api/contracts.js`; `api/adapter.js` maps the
  current mock `data.jsx` → these snake_case keys, guarded by `api/adapter.test.js`. When you point at
  the live API, the adapters become identity (or are deleted) — responses already match.
- Set `DATA_SOURCE=live` and `API_BASE_URL={…}/v1`; attach `Authorization: Bearer` from login, refresh
  on `401` via `/v1/auth/refresh`.
- Only send `X-Tenant-Id` when operating an **impersonation** token (§3.10); normal Catre calls are
  platform-scoped.
- Money values are whole-rupee in current data but typed `decimal(18,2)` server-side — don't assume integer paisa.
- Lists are **cursor-paged**; replace any offset/page-number assumptions with `next_cursor`.
```
