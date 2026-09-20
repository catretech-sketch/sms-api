# Create / Forgot Password — Frontend Integration

**Date:** 2026-06-22
**Backend branch:** `phase-0-foundation`
**Audience:** Catre Admin and School Admin frontend apps (the same flow works for every app — teacher, student/parent, staff — since all hit the shared `/v1/auth` API).

Lets a user set a password by proving control of their **registered email or phone** with a one-time code (OTP). Covers both cases — **first-time "create password"** (account exists, no password yet) and **"forgot password"** (reset an existing one). They are the same flow.

**Conventions (same as the rest of the API):**
- All success bodies are wrapped in a `{ "data": ... }` envelope. All JSON keys are `snake_case`.
- Errors are `{ "error": { "code": "...", "message": "..." } }`.
- All auth endpoints are under `/v1/auth` and share the `auth` rate limiter — **5 requests/min/IP in production** (higher in local dev). Exceeding it returns `429` `{ "error": { "code": "rate_limited", "message": "Too many requests." } }`.
- An `@` in `identifier` routes the OTP via **email**; otherwise via **SMS** (SMS delivery is still a console stub — use email for admin apps).

---

## The flow (2 steps)

```
[Forgot/Create Password screen]
   user enters email (or phone)
          │
          ▼
   POST /v1/auth/password/forgot   ──► 404 if not registered (show "no account")
          │ 200 sent
          ▼
[Enter code + new password screen]
   user enters the 6-digit code + new password (twice)
          │
          ▼
   POST /v1/auth/password/reset    ──► 401 bad code · 422 weak password
          │ 204 success
          ▼
[Login screen]  ← NOT auto-logged-in. User signs in with the new password.
```

---

## Step 1 — request the code

```
POST /v1/auth/password/forgot
Content-Type: application/json

{ "identifier": "admin@school.com" }
```

**Registered → `200`:**
```json
{ "data": { "sent": true } }
```
A 6-digit code is sent (email or SMS) and is valid for **10 minutes**.

**Not registered → `404`:**
```json
{ "error": { "code": "not_registered", "message": "Email is not registered." } }
```
(For a phone identifier the message is `"Phone is not registered."`.)

> Note: this endpoint **does reveal whether an account exists** (404 vs 200). That is an intentional product decision for clearer admin login UX. Surface it directly in the UI (e.g. "We couldn't find an account for that email").

---

## Step 2 — submit the code + new password

```
POST /v1/auth/password/reset
Content-Type: application/json

{
  "identifier": "admin@school.com",
  "code": "123456",
  "password": "myNewPassword1"
}
```

**Success → `204 No Content`** (empty body). The password is now set.
**There is no auto-login** — no tokens are returned. Send the user to the login screen to sign in with the new password via `POST /v1/auth/login`.

**Failure responses:**

| Status | `error.code` | When | Suggested UI |
|--------|--------------|------|--------------|
| `422` | `weak_password` | `password` is shorter than 8 characters | Inline "Password must be at least 8 characters" |
| `401` | `invalid_code` | code is wrong, missing, expired, or already used | "That code is invalid or expired — request a new one" |
| `429` | `rate_limited` | too many auth requests from this IP | "Too many attempts, try again in a minute" |

The OTP is **single-use**: once a reset succeeds (or the code is verified), it is consumed and cannot be reused. If the user mistypes, they keep the same code until it expires (10 min); after expiry they must hit Step 1 again.

---

## Login after reset

```
POST /v1/auth/login
Content-Type: application/json

{ "email": "admin@school.com", "password": "myNewPassword1" }
```
Success `200`:
```json
{ "data": { "access_token": "eyJhbGci...", "refresh_token": "b64-opaque-token" } }
```
Then send `Authorization: Bearer <access_token>` on every `/v1/...` request. Access token TTL ~15 min; refresh via `POST /v1/auth/refresh` (rotating — always store the newly returned refresh token).

---

## Validation to do on the frontend (before calling the API)

- **Password ≥ 8 characters**, and confirm it matches a "confirm password" field. (The backend only enforces the 8-char minimum; everything else is your UX.)
- **Code is 6 digits.** Trim whitespace.
- Trim the `identifier`; lowercase emails before sending if you want consistent matching.

---

## Example (fetch)

```ts
const BASE = "/v1/auth"; // same origin, or your API base URL

async function requestResetCode(identifier: string) {
  const r = await fetch(`${BASE}/password/forgot`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ identifier }),
  });
  if (r.status === 200) return { ok: true as const };
  const body = await r.json();                 // { error: { code, message } }
  return { ok: false as const, code: body.error.code, message: body.error.message };
}

async function resetPassword(identifier: string, code: string, password: string) {
  const r = await fetch(`${BASE}/password/reset`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ identifier, code, password }),
  });
  if (r.status === 204) return { ok: true as const };       // success — go to /login
  const body = await r.json();                                // { error: { code, message } }
  return { ok: false as const, code: body.error.code, message: body.error.message };
}
```

---

## "Create password" vs "Forgot password" — same endpoints

There is no separate first-time endpoint. A user who has never set a password and a user who forgot theirs use the **exact same two calls**. You can label the screen "Create password" or "Forgot password" depending on entry point; the backend behaves identically. (Already-logged-in users who just want to change their password can instead call the existing `POST /v1/auth/set-password` with a Bearer token — no OTP needed.)

---

## Quick reference

| Endpoint | Body | Success | Failures |
|----------|------|---------|----------|
| `POST /v1/auth/password/forgot` | `{ identifier }` | `200 { data: { sent: true } }` | `404 not_registered`, `429 rate_limited` |
| `POST /v1/auth/password/reset` | `{ identifier, code, password }` | `204` (no body, no tokens) | `422 weak_password`, `401 invalid_code`, `429 rate_limited` |
| `POST /v1/auth/login` | `{ email, password }` | `200 { data: { access_token, refresh_token } }` | `401 invalid_credentials`, `422` |
