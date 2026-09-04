# Fee Payment Production Hardening — Design

**Date:** 2026-09-04
**Status:** Draft
**Scope:** `sms-backend` (primary), `sms-admin` (frontend already ships the client for this)

## Summary

The student-fee payment flow has two problems discovered during an audit:

1. **Broken, not just weak:** `sms-admin`'s `feePayments.ts` already calls
   `POST /fees/invoices/{id}/razorpay/order` and
   `POST /fees/invoices/{id}/razorpay/verify`. Neither endpoint exists in
   `sms-backend` — only `PlanUpgradeController` has Razorpay wiring, and that's
   for SaaS plan billing, not student fees. Any parent/staff paying a fee
   online today gets a 404.
2. **No idempotency, no audit trail:** the manual/offline payment path
   (`FeeService.CreatePaymentAsync` → `FeeRepository.CreateAsync`) has no
   duplicate-payment protection, and no action anywhere in the backend writes
   an audit record — there is no `AuditLog` table at all.

This spec fixes both, reusing existing, already-correct infrastructure
(`RazorpayGateway`'s HMAC-SHA256 signature verification, and
`FeeInvoiceRepository.RecordInvoicePaymentAsync`'s `UPDLOCK`/`HOLDLOCK`
transaction) rather than rebuilding them.

**Out of scope:** refunds (no refund endpoint exists yet for student fees —
building one is a new feature, not a hardening fix), fee reminders/reports,
and every non-Fees module. Audit logging is being introduced here as a
minimal, reusable mechanism — Fees is its first and only consumer in this
spec; other modules adopt it in their own future specs.

## 1. Razorpay wiring for fee invoices

Add two `FeeController` actions, mirroring the routes `sms-admin` already
calls:

- `POST /fees/invoices/{id}/razorpay/order` (staff or linked parent, same
  authorization as the existing `POST /fees/invoices/{id}/pay`) — loads the
  invoice, computes the outstanding balance, calls the existing
  `RazorpayGateway` to create a Razorpay order for that amount, and returns
  `{ orderId, amount, currency, keyId }` (shape already matches
  `FeeRazorpayOrder` in the frontend).
- `POST /fees/invoices/{id}/razorpay/verify` — accepts
  `{ razorpayOrderId, razorpayPaymentId, razorpaySignature }`, verifies the
  signature server-side via `RazorpayGateway.VerifyPaymentSignature` (already
  implemented, HMAC-SHA256, constant-time compare), and on success calls
  `FeeInvoiceRepository.RecordInvoicePaymentAsync` with `Method = "razorpay"`
  and `Ref = razorpayPaymentId` — the same transactional, `UPDLOCK`/`HOLDLOCK`
  path that offline payments already use.

`StubPaymentGateway` remains registered only for non-production
environments/tests where no real Razorpay credentials are configured; the
DI registration is switched to `RazorpayGateway` for the fee payment path in
the same way `PlanUpgradeService` already resolves it.

## 2. Idempotency

**Online (Razorpay):** `razorpayPaymentId` is globally unique per Razorpay
payment. Add a unique index on `FeePayments.Ref` (nullable, filtered to
non-null so offline payments without a ref aren't constrained). If `/verify`
is called twice with the same `razorpayPaymentId` (retry, double-tab,
webhook-replay-style client bug), the second insert hits the unique
constraint; the handler catches that specific violation and returns the
existing payment row instead of erroring.

**Offline (manual staff-recorded cash/cheque/UPI-manual):** add a
client-generated `IdempotencyKey` (GUID) column to `FeePayments`, sent once
per form submission (`sms-admin`'s payment form generates it on open, not on
each click). Unique index on `(TenantId, IdempotencyKey)`. `CreateAsync`
becomes insert-or-return-existing on conflict, same pattern as above.

Both cases: the unique-constraint-violation-to-idempotent-response mapping
lives in `FeeRepository`/`FeeInvoiceRepository`, not in the controller, so
callers can't bypass it.

## 3. Minimal reusable audit log

New table `AuditLogs` (migration `M0173`, the next available number as of
this writing):

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | RLS-filtered like other tenant tables |
| ActorUserId | uniqueidentifier | nullable (system-initiated actions) |
| Action | nvarchar(100) | e.g. `FeePayment.Recorded`, `FeePayment.Refunded` |
| Module | nvarchar(50) | e.g. `Fees` |
| EntityType | nvarchar(100) | e.g. `FeePayment` |
| EntityId | nvarchar(100) | |
| TimestampUtc | datetime2 | default `SYSUTCDATETIME()` |
| BeforeData | nvarchar(max) | JSON, nullable |
| AfterData | nvarchar(max) | JSON |

No update/delete path is exposed through the application — rows are
insert-only, written by an `IAuditLogger.LogAsync(...)` helper in
`Sms.Shared.Kernel`.

Both `RecordInvoicePaymentAsync` (online + offline payments) and the
idempotent-duplicate-detection path write their audit row **inside the same
transaction** as the payment/invoice update — a rollback removes both, a
commit keeps both. No app-level CRUD can edit or delete an `AuditLogs` row.

## 4. Schema fixes

Migration adds to `FeePayments`: `CreatedAt datetime2 default
SYSUTCDATETIME()`, `UpdatedAt datetime2 null`, `Ref` unique filtered index,
`IdempotencyKey uniqueidentifier null` + unique filtered index on
`(TenantId, IdempotencyKey)`.

## 5. Tests

Extend `tests/Sms.Tests.Integration/Finance/FeesTests.cs`:

- Razorpay order creation + verify happy path records a payment and updates
  invoice balance.
- Verify with a tampered/invalid signature is rejected, no payment recorded.
- Duplicate `/verify` call with the same `razorpayPaymentId` returns the
  original payment, does not create a second row.
- Duplicate manual payment submission (same `IdempotencyKey`) returns the
  original payment, does not create a second row.
- An `AuditLogs` row is created for both online and offline payment
  recording, with correct `TenantId`/`Action`/`EntityId`.

## Frontend impact

None required — `sms-admin`'s `feePayments.ts` already calls the correct
routes and shapes; this spec makes the backend match what the frontend
already expects.
