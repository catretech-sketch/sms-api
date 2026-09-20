# OTP-gated Password Create / Reset

**Date:** 2026-06-22
**Status:** Approved (design)
**Scope:** Backend only (`Sms.Api`). Serves all client apps (catre-admin, school-admin,
teacher, principal, student/parent, staff) through the shared `/v1/auth` API.

## Problem

Users need a self-service way to set a password when they have none yet
("create password", first login) or have forgotten an existing one ("forgot
password"). Both must be gated by proving control of a registered email or phone
via a one-time code (OTP).

The backend already has the building blocks — `/v1/auth/otp/request` (send OTP to a
registered identifier), `/v1/auth/otp/verify` (validate OTP, issue a session), and
`/v1/auth/set-password` (set the authenticated user's password). This work adds two
**dedicated, single-purpose endpoints** so clients have a clear password-reset
contract that does **not** double as a login.

## Decisions

- **One unified pair** of endpoints serves both first-time create and forgot-reset —
  they are the same operation on the backend (set a password after OTP proof).
- **No auto-login.** A successful reset returns `204 No Content`; the user then logs
  in normally with the new password.
- **Password policy:** minimum 8 characters. No other complexity requirement.
- **No DB migration, no new repository methods.** Reuses the existing `dbo.OtpCodes`
  table (via `Otp_Insert` / `Otp_GetActive` / `Otp_Consume`) and `dbo.User_SetPassword`.

## Endpoints

Both live under `app.MapGroup("/v1/auth").RequireRateLimiting("auth")` in
`src/Sms.Api/Endpoints/AuthEndpoints.cs`.

### `POST /v1/auth/password/forgot`

Request body:

```json
{ "identifier": "user@example.com" }   // email (contains '@') or phone
```

Behavior:

1. Set a system/platform tenant session (`tenant.Set(null, null, isPlatform: true)`) —
   `dbo.Users` is RLS-protected and the caller is anonymous.
2. Resolve the user by email (if `identifier` contains `@`) or phone.
3. **Not registered → `404` `not_registered`** with message
   `"Email is not registered."` / `"Phone is not registered."` (identical to the
   existing `/otp/request`).
4. **Registered →** generate an OTP, send it on the matching channel
   (`email` / `sms`), store its SHA-256 hash with a 10-minute expiry, and return
   `200 { "sent": true }`.

The send logic (steps 2-4) is **extracted into a private helper** shared with the
existing `/otp/request` endpoint to avoid duplication.

### `POST /v1/auth/password/reset`

Request body:

```json
{ "identifier": "user@example.com", "code": "123456", "password": "newSecret1" }
```

Behavior:

1. **`password.Length < 8` → `422` `weak_password`** (`"password must be at least 8 characters"`).
2. System/platform session; read the active OTP hash for the identifier.
   **Missing or `!= sha256(code)` → `401` `invalid_code`** (`"code invalid or expired"`).
3. Consume the OTP (`Otp_Consume`).
4. Resolve the user (email/phone). Missing → `401` `invalid_code` (defensive; the OTP
   was issued against a registered identifier).
5. Set the password hash via `User_SetPassword`.
6. Return **`204 No Content`** — no tokens issued.

## Models (`src/Sms.Api/Auth/LoginModels.cs`)

```csharp
public sealed record ForgotPasswordRequest(string Identifier);
public sealed record ResetPasswordRequest(string Identifier, string Code, string Password);
```

## Deliberate trade-offs

- **Shared OTP, no per-purpose scoping.** A reset OTP and a login OTP are
  interchangeable (the `OtpCodes` row is keyed by identifier + channel only). This adds
  **no** privilege: completing an OTP already yields a full session via `/otp/verify`,
  from which `set-password` works today. Purpose-scoping would be added complexity for
  no security gain — omitted (YAGNI).
- **Account enumeration retained.** The `404 not_registered` response reveals whether
  an account exists. Kept **only** for consistency with the existing `/otp/request`,
  which documents this as an intentional product choice for login UX. Can be changed
  later to a uniform `200 { sent: true }` if enumeration becomes a concern.

## Testing

Integration tests in `tests/Sms.Tests.Integration/Saas/PasswordResetTests.cs`,
mirroring `OtpLoginTests` (insert a user directly, then overwrite the stored
`OtpCodes.CodeHash` with the hash of a known code `"123456"` to stay deterministic):

| Case | Expected |
| --- | --- |
| `forgot` with unregistered identifier | `404 not_registered` |
| `forgot` with registered email | `200 { sent: true }`, OTP row exists |
| `reset` with valid code + valid password | `204`, **then `/login` with the new password → `200`** |
| `reset` with wrong code | `401 invalid_code` |
| `reset` with password `< 8` chars | `422 weak_password` |
| `reset` success | response body carries no `access_token` |

## Out of scope

- Frontend wiring in the client apps (separate work per repo).
- Changing the existing `/otp/request`, `/otp/verify`, or `/set-password` contracts
  (only a non-behavioral helper extraction in `/otp/request`).
- OTP delivery configuration (SMTP credentials, SMS provider).
