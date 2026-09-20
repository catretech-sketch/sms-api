# SMS Backend — Real Email OTP Delivery (SMTP) — Design Spec

> **Status:** Approved design (2026-06-17). Replaces the `ConsoleOtpSender` stub with **real email
> OTP delivery via SMTP** (platform-wide). **SMS delivery stays stubbed** until a provider is named.
> Builds on the existing SaaS auth (`docs/superpowers/specs/2026-06-15-phase-0.5-production-hardening-design.md`)
> and the OTP login flow already shipped (`/v1/auth/otp/request`, `/v1/auth/otp/verify`).

## Context

The OTP login flow is complete and routes by identifier: an `@` → `email` channel, otherwise `sms`.
Both channels currently resolve to `ConsoleOtpSender`, which generates a 6-digit code and writes it to
the console (`src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs`) — a "Track C" stub. The `IOtpSender`
contract is: `SendAsync(identifier, channel)` generates the code, sends it, and returns the plaintext;
the endpoint hashes + stores it (`AuthEndpoints.cs:64-82`). The sender owns code generation.

Registration is a plain `AddSingleton<IOtpSender, ConsoleOtpSender>()` (`Program.cs:54`) — no
config-based provider swap exists.

**Decisions locked during brainstorming (2026-06-16/17):**
- **Email provider:** generic **SMTP** (MailKit). Dev account: Gmail (`smtp.gmail.com:587`, STARTTLS,
  `catre.tech@gmail.com` with an app password).
- **SMS provider:** deferred — `sms` channel keeps `ConsoleOtpSender` until the provider is named.
- **Send model:** **background worker**. The endpoint generates + stores the code synchronously and
  returns immediately; the actual SMTP send is enqueued to an in-memory queue and performed out-of-band
  by a hosted `BackgroundService`. Decouples login latency from Gmail; allows send retries.
- **Config scope:** **platform-wide** — one set of SMTP credentials for the whole platform.
- **Secrets:** non-secret SMTP settings in `appsettings.json` (placeholder password); real password via
  **`dotnet user-secrets` in dev / env var in prod** — consistent with the established secrets policy.
  The shared dev app password will be **rotated** after verification (it was disclosed in plaintext).

**Non-goals:** no SMS delivery; no per-tenant credentials; no change to the `IOtpSender` contract, the
OTP endpoints, or the OTP request/verify/hash/store logic; no new email templating engine.

---

## Architecture

The change lives entirely **behind the `IOtpSender` interface** — endpoints are untouched. New types in
`src/Sms.Shared.Kernel/Auth/`:

### 1. `SmtpOptions`
Config record bound from a new `Smtp` section:
`Host`, `Port` (int), `User`, `Password`, `From`, `UseStartTls` (bool, default true).

### 2. `EmailMessage` + `IEmailQueue` + `EmailQueue`
The enqueue seam between request and worker.
```
public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
    ValueTask<EmailMessage> DequeueAsync(CancellationToken ct);
}
```
`EmailQueue` wraps an unbounded `System.Threading.Channels.Channel<EmailMessage>` — non-blocking
`Enqueue` (writer), awaited `DequeueAsync` (reader). Singleton. **Unit-testable** (enqueue → dequeue
round-trip).

### 3. `IEmailSender` + `SmtpEmailSender`
Thin transport seam over MailKit:
```
Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
```
`SmtpEmailSender` connects to `Host:Port` (STARTTLS per `UseStartTls`), authenticates with `User`/
`Password`, sends a `MimeMessage` from `From`. This is the **only untested I/O shim** (MailKit's
`SmtpClient` is concrete and needs a network). Used **only by the worker**, never by the request path.

### 4. `EmailOtpSender : IOtpSender`
- Generates a cryptographically-random 6-digit code (`RandomNumberGenerator.GetInt32`), matching the
  console stub's format.
- Builds the OTP subject + body and **enqueues** an `EmailMessage` via `IEmailQueue.Enqueue` (does
  **not** send — returns without blocking on SMTP).
- Returns the plaintext code.
- **Unit-testable** with a fake `IEmailQueue`: asserts 6-digit code format and that the enqueued body
  contains the code and is addressed to the identifier.

### 5. `EmailDispatchWorker : BackgroundService`
Hosted service registered with `AddHostedService`. Loop: `await queue.DequeueAsync(stoppingToken)` →
`IEmailSender.SendAsync(...)` inside a try/catch. On failure, retry up to **3 attempts** with short
backoff, then log an error and drop (the OTP expires in 10 min; no dead-letter for now). A send failure
must never crash the loop. **Unit-testable**: enqueue one message, run one drain iteration with a fake
`IEmailSender`, assert it was sent; assert a throwing sender doesn't kill the loop.

### 6. `ChannelOtpSender : IOtpSender`
Routing sender registered as `IOtpSender`:
- `channel == "email"` → `EmailOtpSender`
- otherwise (`"sms"`) → `ConsoleOtpSender` (stub, unchanged)
- **Unit-testable**: email routes through the email sender; sms routes through the console stub.

### Data flow (endpoint unchanged)
```
POST /v1/auth/otp/request
  → IOtpSender.SendAsync(identifier, channel)        // now ChannelOtpSender
      → "email" → EmailOtpSender: gen code, Enqueue(EmailMessage), return code
      → "sms"   → ConsoleOtpSender (stub)
  → returns plaintext code → endpoint hashes (SHA256) + stores with 10-min expiry → 200

EmailDispatchWorker (background)
  → DequeueAsync → SmtpEmailSender.SendAsync → Gmail SMTP   (retry x3, log on failure)
```

---

## Configuration

`appsettings.json` (committed, placeholder password):
```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "UseStartTls": true,
  "User": "catre.tech@gmail.com",
  "From": "catre.tech@gmail.com",
  "Password": ""
}
```
Real password (dev): `dotnet user-secrets set "Smtp:Password" "<app-password>"` in `src/Sms.Api`.
Prod: `Smtp__Password` env var.

---

## DI wiring (`Program.cs`)

Replace line 54 with:
```csharp
builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());
builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<EmailOtpSender>();
builder.Services.AddSingleton<ConsoleOtpSender>();
builder.Services.AddSingleton<IOtpSender, ChannelOtpSender>();
builder.Services.AddHostedService<EmailDispatchWorker>();
```

---

## Testing (TDD)

- `EmailQueueTests` — enqueue → `DequeueAsync` returns the same message.
- `EmailOtpSenderTests` — 6-digit code for email channel; enqueued body contains the code; recipient
  is the identifier. (fake `IEmailQueue`)
- `EmailDispatchWorkerTests` — a queued message is handed to `IEmailSender`; a throwing sender does not
  crash the drain loop. (fake `IEmailSender` + real/fake queue, single iteration via cancellation)
- `ChannelOtpSenderTests` — email channel uses email sender; sms channel uses console stub.
- Existing `ConsoleOtpSenderTests` stays green.
- `SmtpEmailSender` real send: **not** a unit test; verified by an opt-in manual run against Gmail.

---

## Risks

- **Gmail app password disclosed in plaintext** → rotate after verification. Never commit it.
- **Gmail send limits / spam classification** → acceptable for dev; production may switch SMTP host via
  config only (no code change).
