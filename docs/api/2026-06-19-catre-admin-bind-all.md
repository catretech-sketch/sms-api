# Catre Admin — End-to-End Binding Brief (for the frontend agent)

**Date:** 2026-06-19  **Backend branch:** `phase-0-foundation`
**Audience:** the frontend Claude wiring the **Catre platform super-admin** app to the live backend.
**Status:** **Implementation truth.** Every endpoint below is *actually implemented and running* (verified against `src/Sms.Modules.Tenancy/ModuleEndpoints.cs` + contracts). This supersedes the older `catreadmin-api.md` "forward contract" (which was written when only auth/health existed). Where this doc and the forward contract disagree, **this doc wins** — bind to what's described here.

> **Your job:** replace the Catre admin app's mock data layer with real HTTP calls to these endpoints, end to end — auth, then every resource. Keep the app's existing camelCase domain models if it has them; add a thin mapper at the HTTP boundary, because **the wire is snake_case**.

---

## 0. TL;DR for binding

1. Implement the **OTP login** flow (§2) → store `access_token` + `refresh_token`.
2. Add an HTTP client that: sets `Authorization: Bearer <access_token>` on every `/v1/*` call, unwraps the `{ "data": ... }` envelope, and on `401` runs the **refresh** rotation (§2) then retries once.
3. Bind resources in this order (low-risk → high): dashboard → clients → plans → subscriptions → invoices → onboarding → tickets → team → audit → reports.
4. Treat snake_case as the wire format; convert at the boundary only.

---

## 1. Conventions (apply to every endpoint)

| Concern | Rule |
|---|---|
| Base URL | `{API_BASE_URL}/v1` — local dev: `http://localhost:5162/v1` |
| Casing | **snake_case** for every JSON key, request and response |
| Auth | `Authorization: Bearer <access_token>` on **every** `/v1/*` call except `/v1/auth/otp/*`, `/v1/auth/refresh`, `/v1/auth/login` |
| Tenant header | Catre staff are **platform** users (RLS bypass). Do **not** send `X-Tenant-Id` — these endpoints don't need it. |
| Single object | `{ "data": { ... } }` |
| List (paged) | `{ "data": [ ... ], "next_cursor": "…" \| null }` — see the ⚠️ paging note below |
| List (unpaged) | `{ "data": [ ... ] }` — **no** `next_cursor` key (plans, team, onboarding) |
| Error | `{ "error": { "code": "…", "message": "…", "details": { … } \| null } }` |
| Dates | ISO-8601 UTC — `YYYY-MM-DD` (date-only), `YYYY-MM-DDThh:mm:ssZ` (timestamps) |
| Money | INR `decimal(18,2)`. Treat as exact decimals; don't do float math client-side. |
| Auth rate limit | `/v1/auth/*` is limited to **5 req/min/IP** (`429` on exceed). |

⚠️ **Paging reality:** the list endpoints that return the `{data, next_cursor}` shape (clients, invoices, subscriptions, tickets, audit) currently **always return `next_cursor: null`** — server-side cursor paging is wired in the response shape but not yet emitting cursors. Build the UI to read `next_cursor` and stop when it's `null` (forward-compatible), but expect a single page today. The `limit`/`cursor` query params are accepted but not yet honored.

**Error codes you'll see:** `not_found` (404), `conflict` (409), `invalid_code` (401, OTP), `forbidden` (403, non-platform token), `rate_limited` (429). Bodies always follow the `error` envelope above.

---

## 2. Auth flow (Catre admin login) — bind this first

All under `/v1/auth`. The seeded platform admin identity is **`catre.tech@gmail.com`** (auto-provisioned on backend boot). Login is **email OTP — no password**.

### Step 1 — request OTP
```
POST /v1/auth/otp/request
{ "identifier": "catre.tech@gmail.com" }
```
Always `200` (never leaks account existence):
```json
{ "data": { "sent": true } }
```
An `@` in `identifier` routes via **email**; otherwise SMS (SMS is a stub — use email for Catre admin). The 6-digit code is valid **10 minutes**.

> **Local dev shortcut:** in `Development`, the backend also logs the code to the API console as
> `[DEV OTP/email] <identifier> -> <code>`, so you can sign in without working SMTP. (Real SMTP delivery needs `Smtp:Password` set; it's empty by default.)

### Step 2 — verify → tokens
```
POST /v1/auth/otp/verify
{ "identifier": "catre.tech@gmail.com", "code": "123456" }
```
`200`:
```json
{ "data": { "access_token": "eyJ…", "refresh_token": "b64-opaque" } }
```
`401`: `{ "error": { "code": "invalid_code", "message": "code invalid or expired" } }`

The access token is a JWT carrying `is_platform=1` — that claim is what authorizes every `/v1` Catre endpoint. **Access TTL ~15 min, refresh TTL 30 days.**

### Step 3 — authorized calls
Send `Authorization: Bearer <access_token>` on every `/v1/...`. A non-platform token → `403`.

### Refresh (rotating — store the new one!)
```
POST /v1/auth/refresh
{ "refresh_token": "<current>" }
```
→ `{ "data": { "access_token": "…", "refresh_token": "<new>" } }`. The old refresh token is **revoked on use** — always persist the newly returned `refresh_token`.

### Optional — set a password
After OTP login, the admin may set a password to also enable `POST /v1/auth/login` (email+password). Not required for the app to work.
```
POST /v1/auth/set-password   (Bearer)
{ "password": "…" }   → 204 No Content
```

---

## 3. Resource endpoints

All require a platform Bearer token. Field names below are the **exact wire (snake_case) keys**.

### 3.1 Clients (the tenant/school records)

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/clients` | Query: `status`, `tier`, `q` (name search). Returns `{data:[client], next_cursor}`. |
| `GET` | `/v1/clients/{id}` | `{data: client}` or `404`. |
| `POST`| `/v1/clients` | **Onboard a client.** `201 {data: client}`. See behavior note. |
| `POST`| `/v1/clients/{id}/status` | Body `{ "status", "reason"? }` → `{data: client}` / `404`. |
| `POST`| `/v1/clients/{id}/change-plan` | Body `{ "plan_id" }` → `{data: client}` / `404`. |

**`POST /v1/clients` body** (`CreateClientRequest`):
```json
{
  "name": "Springfield High",
  "slug": "springfield-high",
  "country": "IN",
  "admin_name": "Karthik R",
  "admin_email": "admin@springfield.example",
  "admin_phone": "9876543210",
  "plan_id": "0f9a…-uuid",
  "trial_days": 14,
  "csm": "Priya"
}
```
> **Behavior to know:** this creates the tenant **and**, *only if* `admin_email` or `admin_phone` is present, provisions the school-admin login user (role `school_admin`). If you want the new school to be able to log in, you **must** pass at least one of those. `country`, `admin_name`, `admin_phone`, `csm` are nullable.

**`client` object** (`ClientResponse`):
```json
{
  "id": "uuid", "name": "…", "slug": "…", "country": "IN", "status": "active",
  "plan_id": "uuid|null", "plan_name": "Growth|null", "tier": "growth|null", "mrr": 14999.00,
  "students_count": 420, "staff_count": 35, "storage_gb": 12.5,
  "limits": { "students": 500, "staff": 50, "storage_gb": 20 },
  "created": "2026-06-19T09:12:00Z", "csm": "Priya|null", "health_score": 82
}
```
`limits.*` may be `null` (no limit set).

### 3.2 Plans (subscription tiers)

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/plans` | Query: `visibility`, `audience`. **Unpaged** → `{data:[plan]}` (no `next_cursor`). |
| `GET` | `/v1/plans/{id}` | `{data: plan}` / `404`. |
| `POST`| `/v1/plans` | **Upsert** (create or update). `201 {data: plan}`. |

**`POST /v1/plans` body** (`PlanUpsertRequest`): include `id` to update, omit/null to create.
```json
{
  "id": null,
  "name": "Growth", "tier": "growth", "pricing": "per_student", "price": 0,
  "per_student": 35.00, "min_students": 100, "period": "month",
  "features": ["attendance", "fees", "transport"],
  "limits": { "students": 500, "staff": 50, "storage_gb": 20 },
  "visibility": "public", "audience": "schools", "band": null,
  "offer": { "label": "Launch 20% off", "pct": 20 },
  "color": "#4F46E5", "description": "…"
}
```
**`plan` object** (`PlanResponse`): same fields as above plus a non-null `id`. `features` is an array (empty `[]` if none). `offer` is `null` when there's no offer. `per_student`, `min_students`, `band`, `offer`, `color`, `description` are nullable.

### 3.3 Subscriptions

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/subscriptions` | Query: `status`, `tenant_id`. `{data:[sub], next_cursor}`. |
| `GET` | `/v1/subscriptions/{id}` | `{data: sub}` / `404`. |
| `POST`| `/v1/subscriptions` | Body `{ "tenant_id", "plan_id", "seats" }` → `201 {data: sub}`. |

**`sub` object** (`SubscriptionResponse`):
```json
{ "id":"uuid", "tenant_id":"uuid", "plan_id":"uuid", "status":"active",
  "started_at":"2026-06-01T00:00:00Z", "renews_at":"2026-07-01T00:00:00Z|null", "seats": 50 }
```

### 3.4 Invoices

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/invoices` | Query: `status`, `tenant_id`. `{data:[invoice], next_cursor}`. |
| `GET` | `/v1/invoices/{id}` | `{data: invoice}` / `404`. |
| `POST`| `/v1/invoices/{id}/mark-paid` | `{data: invoice}` / `404`. |
| `POST`| `/v1/invoices/{id}/refund` | `{data: invoice}`; `404` if missing, `409 conflict` if invoice not `paid`. |

**`invoice` object** (`InvoiceResponse`):
```json
{ "id":"uuid","tenant_id":"uuid","tenant_name":"…|null","plan_name":"…|null",
  "amount": 14999.00, "status":"paid", "issued":"2026-06-01T…Z", "due":"2026-06-15T…Z", "paid_on":"2026-06-10T…Z|null" }
```
> Payment is a **stub** — `mark-paid`/`refund` mutate the record but no money moves.

### 3.5 Onboarding pipeline (CRM stage tracker — NOT tenant creation)

> ⚠️ This is a deal/stage tracker, distinct from `POST /v1/clients`. Advancing an onboarding item does **not** create a working tenant. Use `/v1/clients` for real provisioning.

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/onboarding` | Query: `stage`. **Unpaged** → `{data:[item]}`. |
| `POST`| `/v1/onboarding` | Body `{ "name","slug","owner"?,"value","stage"? }` → `201 {data: item}`. |
| `POST`| `/v1/onboarding/{id}/advance` | Body `{ "stage" }` → `{data: item}` / `404`. |
| `PATCH`| `/v1/onboarding/{id}/checklist` | Body `{ "label","done" }` (upserts one checklist item) → `{data: item}` / `404`. |

**`item` object** (`OnboardingItemResponse`):
```json
{ "id":"uuid","tenant_id":"uuid|null","name":"…","slug":"…","owner":"…|null",
  "value": 30000.00, "stage":"demo",
  "checklist":[ {"label":"Contract sent","done":true} ], "done": 2, "age": 5 }
```
`done` = count of completed checklist items; `age` = days since created.

### 3.6 Support tickets

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/tickets` | Query: `status`, `q`. `{data:[ticket_summary], next_cursor}`. |
| `GET` | `/v1/tickets/{id}` | `{data: ticket_detail}` (includes `messages[]`) / `404`. |
| `POST`| `/v1/tickets` | Body `{ "subject","tenant_id"?,"tenant_name"?,"priority"? }` → `201 {data: ticket_detail}`. |
| `PATCH`| `/v1/tickets/{id}` | Body `{ "status"?, "assignee"? }` → `{data: ticket_summary}` / `404`. |
| `POST`| `/v1/tickets/{id}/messages` | Body `{ "text" }` → `201 {data: ticket_detail}`. `who` is taken from the token. |

**`ticket_summary`** (`TicketResponse`):
```json
{ "id":"uuid","subject":"…","tenant_id":"uuid|null","tenant_name":"…|null",
  "status":"open","priority":"normal","assignee":"…|null",
  "created":"…Z","updated":"…Z","messages_count": 3 }
```
**`ticket_detail`** = `ticket_summary` + `"messages": [ { "id","ticket_id","who":"…|null","role","text","when":"…Z" } ]`.

### 3.7 Team (Catre internal staff)

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/team` | **Unpaged** → `{data:[member]}`. |
| `POST`| `/v1/team` | Body `{ "name","email","role" }` → `201 {data: member}`. |
| `PATCH`| `/v1/team/{id}` | Body `{ "role"?, "status"? }` → `{data: member}` / `404`. |

**`member`** (`TeamMemberResponse`):
```json
{ "id":"uuid","name":"…","email":"…","role":"admin","status":"active",
  "last_login":"…Z|null","joined":"…Z" }
```

### 3.8 Audit log

| Method | Path | Notes |
|---|---|---|
| `GET` | `/v1/audit` | Query: `kind`, `actor_id`, `tenant_id`. `{data:[entry], next_cursor}`. |

**`entry`** (`AuditEntry`):
```json
{ "id":"uuid","actor_id":"uuid|null","actor_name":"…|null","role":"…|null",
  "action":"client.created","target":"Springfield High|null","kind":"client|null","time":"…Z" }
```

### 3.9 Dashboard & reports

| Method | Path | Returns |
|---|---|---|
| `GET` | `/v1/dashboard/overview` | `{data: DashboardOverview}` — see shape below. |
| `GET` | `/v1/reports/revenue` | `{data: RevenueReport}` — see shape below. |
| `GET` | `/v1/reports/clients.csv` | **CSV file** (`text/csv`, `Content-Disposition: attachment; filename="catre-clients.csv"`). Columns: `client,status,plan,mrr,students,staff,country,created`. Not enveloped — it's a raw download. |

**`DashboardOverview`:**
```json
{
  "counts": { "total":12,"active":8,"trial":2,"suspended":1,"cancelled":1 },
  "mrr": 54000.00, "trials_ending": 2, "churn_pct": 5.00,
  "months": ["Jan","Feb","Mar","Apr","May","Jun"],
  "mrr_series": [40000,44000,47000,50000,52000,54000],
  "signup_series": [3,2,4,1,2,3],
  "plan_mix": [ {"label":"growth","value":5,"color":null} ],
  "usage_alerts": [ {"tenant":"Springfield High","metric":"students","used":95,"limit":100,"pct":95} ],
  "system_health": [ {"name":"Database","status":"operational","latency":"-","uptime":"-"} ],
  "recent_activity": [ {"actor":"Karthik","action":"client.created","target":"Springfield High","kind":"client","at":"…Z"} ]
}
```
- `months`/`mrr_series`/`signup_series` are **6 elements**, oldest→newest, index-aligned (use `months[i]` as the x-axis label).
- `usage_alerts` lists tenants at **≥80%** of a plan limit (`metric` = `"students"` or `"storage"`); may be `[]`.
- `recent_activity` = latest 20 audit entries; `actor` may be `null`.
- `system_health` is a single static "Database operational" row for now.

**`RevenueReport`:**
```json
{
  "arr": 648000.00, "net_growth": 2, "gross_churn_pct": 5.00, "arpa": 6750.00,
  "months": ["Jan","Feb","Mar","Apr","May","Jun"],
  "revenue_series": [38000,41000,45000,47000,51000,53000],
  "revenue_by_plan": [ {"label":"scale","value":3,"color":null} ],
  "plan_performance": [ {"plan_name":"Scale","clients":3,"mrr":30000,"share_pct":55.5} ]
}
```
- `arr` = active MRR × 12; `arpa` = active MRR ÷ active client count.
- `revenue_series[i]` = sum of **paid invoices** in `months[i]`.
- `plan_performance[].share_pct` = plan's % of total active MRR.

---

## 4. Honest limitations the UI must handle

- **Metrics history isn't backfilled.** `mrr_series`, `churn_pct`, `gross_churn_pct`, `net_growth` come from a **monthly snapshot** written going forward (one row/month, refreshed each boot). On a fresh environment only the current month has data: earlier `mrr_series` entries render `0`, and the churn/net-growth figures need **≥2 monthly snapshots** to be meaningful (they read `0` in month 1). Treat a flat/`0` early series as "not enough history yet," **not** an error. Live-computed fields (`counts`, `mrr`, `revenue_series`, `signup_series`, `usage_alerts`, `recent_activity`, `plan_mix`, `plan_performance`) are accurate immediately.
- **Payments are stubbed** — invoice `mark-paid`/`refund` change records, no real charge.
- **SMS OTP is stubbed** — use **email** OTP for Catre admin (fully working).
- **Cursor paging not emitting yet** — `next_cursor` is always `null` today (§1 ⚠️). Build for it; expect one page.

---

## 5. Binding checklist (suggested order)

- [ ] HTTP client: base URL, Bearer injection, `{data}` unwrap, `{error}` surfacing, `401`→refresh→retry-once.
- [ ] OTP login screen → `otp/request` then `otp/verify`; persist both tokens; refresh rotation.
- [ ] Dashboard (`/dashboard/overview`) — read-only, safest first bind.
- [ ] Clients list/detail/create/status/change-plan.
- [ ] Plans list/detail/upsert.
- [ ] Subscriptions, Invoices (incl. mark-paid/refund + `409` handling).
- [ ] Onboarding, Tickets (incl. messages), Team, Audit.
- [ ] Revenue report + CSV export (handle as file download, not JSON).
- [ ] Empty-state + "insufficient history" handling for early-month series.

**Source of truth:** `src/Sms.Modules.Tenancy/ModuleEndpoints.cs`, `Contracts/CatreContracts.cs`, `Contracts/OpsContracts.cs`, `Contracts/BillingContracts.cs`; auth in `src/Sms.Api/Endpoints/AuthEndpoints.cs`.
