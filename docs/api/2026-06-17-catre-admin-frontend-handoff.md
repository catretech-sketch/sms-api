# Catre Admin — Backend Handoff for Frontend

**Date:** 2026-06-17
**Backend branch:** `phase-0-foundation`
**Scope:** Catre **platform admin** app only (the super-admin that manages client schools, plans, billing, tickets — not a school/tenant user).

This documents what changed in the backend so the frontend can align. **All response bodies are wrapped in a `{ "data": ... }` envelope and all JSON keys are `snake_case`.** Errors come back as `{ "error": { "code": "...", "message": "..." } }`.

---

## 1. What changed (summary)

1. **Catre admin can now log in.** A platform admin is auto-provisioned on backend startup (identity `catre.tech@gmail.com`). Login is via **email OTP** (no password needed). Previously there was no way to get a platform admin into the system, so the entire Catre surface was unreachable.
2. **Dashboard and revenue endpoints now return real data.** Fields that were previously empty arrays / hardcoded zeros (`churn_pct`, `mrr_series`, `signup_series`, `usage_alerts`, `recent_activity`, `net_growth`, `gross_churn_pct`, `revenue_series`) are now populated from the database.

No fields were renamed or removed — this is additive. Endpoints that returned empty arrays now return real objects.

---

## 2. Auth flow (Catre admin login)

All auth endpoints are under `/v1/auth` and are rate-limited (5 req/min/IP).

### Step 1 — request an OTP
```
POST /v1/auth/otp/request
Content-Type: application/json

{ "identifier": "catre.tech@gmail.com" }
```
Response is **always** `200` (never leaks whether the account exists):
```json
{ "data": { "sent": true } }
```
The 6-digit code is emailed (valid 10 minutes). An `@` in `identifier` routes via email; otherwise SMS (SMS delivery is still a stub — use email for Catre admin).

### Step 2 — verify the OTP → get tokens
```
POST /v1/auth/otp/verify
Content-Type: application/json

{ "identifier": "catre.tech@gmail.com", "code": "123456" }
```
Success `200`:
```json
{
  "data": {
    "access_token": "eyJhbGci...",
    "refresh_token": "b64-opaque-token"
  }
}
```
Failure `401`: `{ "error": { "code": "invalid_code", "message": "code invalid or expired" } }`

The access token is a JWT carrying `is_platform=1` (this is what authorizes every `/v1` Catre endpoint). Access token TTL ~15 min; refresh token TTL 30 days.

### Step 3 — call platform endpoints
Send `Authorization: Bearer <access_token>` on every `/v1/...` request. A non-platform token gets `403`.

### Refresh (rotating)
```
POST /v1/auth/refresh
{ "refresh_token": "<current refresh token>" }
```
→ `{ "data": { "access_token": "...", "refresh_token": "<new>" } }`. The old refresh token is revoked on use — always store the newly returned one.

### Optional — set a password
After logging in (OTP), the admin may set a password to also enable `POST /v1/auth/login` (email+password). Not required.
```
POST /v1/auth/set-password      (Authorization: Bearer <access_token>)
{ "password": "..." }            → 204 No Content
```

---

## 3. Dashboard overview

```
GET /v1/dashboard/overview        (Authorization: Bearer <platform token>)
```

Full response shape (all keys `snake_case`, wrapped in `data`):
```json
{
  "data": {
    "counts": { "total": 12, "active": 8, "trial": 2, "suspended": 1, "cancelled": 1 },
    "mrr": 54000.00,
    "trials_ending": 2,
    "churn_pct": 5.00,

    "months":        ["Jan", "Feb", "Mar", "Apr", "May", "Jun"],
    "mrr_series":    [40000, 44000, 47000, 50000, 52000, 54000],
    "signup_series": [3, 2, 4, 1, 2, 3],

    "plan_mix": [
      { "label": "growth", "value": 5, "color": null },
      { "label": "scale",  "value": 3, "color": null }
    ],

    "usage_alerts": [
      { "tenant": "Springfield High", "metric": "students", "used": 95, "limit": 100, "pct": 95 },
      { "tenant": "Riverdale School", "metric": "storage",  "used": 18,  "limit": 20,  "pct": 90 }
    ],

    "system_health": [
      { "name": "Database", "status": "operational", "latency": "-", "uptime": "-" }
    ],

    "recent_activity": [
      { "actor": "Karthik", "action": "client.created", "target": "Springfield High", "kind": "client", "at": "2026-06-17T09:12:00Z" }
    ]
  }
}
```

Field notes:
- `months` / `mrr_series` / `signup_series` are always **6 elements** (last 6 calendar months, oldest→newest), index-aligned. Use `months[i]` as the x-axis label for `mrr_series[i]` and `signup_series[i]`.
- `usage_alerts` lists tenants at **≥80%** of a plan limit. `metric` is `"students"` or `"storage"`. May be an empty array (no one near a limit).
- `recent_activity` is the latest 20 audit entries. `actor` may be `null`.
- `system_health` is currently a single static "Database operational" row (a richer health feed is out of scope for now).

---

## 4. Revenue report

```
GET /v1/reports/revenue           (Authorization: Bearer <platform token>)
```
```json
{
  "data": {
    "arr": 648000.00,
    "net_growth": 2,
    "gross_churn_pct": 5.00,
    "arpa": 6750.00,

    "months":         ["Jan", "Feb", "Mar", "Apr", "May", "Jun"],
    "revenue_series": [38000, 41000, 45000, 47000, 51000, 53000],

    "revenue_by_plan": [
      { "label": "scale",  "value": 3, "color": null },
      { "label": "growth", "value": 5, "color": null }
    ],

    "plan_performance": [
      { "plan_name": "Scale",  "clients": 3, "mrr": 30000, "share_pct": 55.5 },
      { "plan_name": "Growth", "clients": 5, "mrr": 24000, "share_pct": 44.5 }
    ]
  }
}
```

Field notes:
- `arr` = active MRR × 12. `arpa` = active MRR ÷ active client count.
- `months` / `revenue_series` are 6 elements, index-aligned (revenue = sum of **paid invoices** per month).
- `plan_performance[].share_pct` is each plan's % of total active MRR.

---

## 5. Honest limitation the frontend should know

`mrr_series`, `churn_pct`, `gross_churn_pct`, and `net_growth` are derived from a **monthly metrics snapshot** that the backend writes going forward (one row per month, refreshed on each boot). **There is no backfilled history**: on a freshly deployed environment, only the current month has data, so:
- `mrr_series` shows real values only for months that have a snapshot (earlier months render as `0`).
- `churn_pct` / `gross_churn_pct` / `net_growth` need at least **two** monthly snapshots to be meaningful — they read `0` in the first month after deployment, then become real from the second month onward.

`revenue_series`, `signup_series`, `usage_alerts`, `recent_activity`, `counts`, `mrr`, `plan_mix`, `plan_performance` are computed live from existing data and are accurate immediately.

The UI should treat a `0`/flat early series as "not enough history yet," not as an error.

---

## 6. Not included (deferred)

- **Real payment gateway** — charging is still a stub. Invoice `mark-paid` / `refund` work on records but no money moves.
- **Real SMS OTP** — SMS sending is a console stub. Catre admin uses **email** OTP, which is fully working.

These are separate, planned workstreams.
