# Fee Payment Production Hardening — Design

**Date:** 2026-09-04
**Status:** Draft
**Scope:** `sms-backend` only

## Summary

Two problems were found while auditing the student-fee payment flow:

1. **Broken online payment path (out of scope for this spec):**
   `sms-admin`'s `feePayments.ts` calls `POST
   /fees/invoices/{id}/razorpay/order` and `POST
   /fees/invoices/{id}/razorpay/verify`. Neither endpoint exists in
   `sms-backend` — only `PlanUpgradeController` has Razorpay wiring, for SaaS
   plan billing, not student fees. Any parent/staff paying a fee online today
   gets a 404. **This spec does not fix that** — real Razorpay integration
   for fee invoices is tracked as a separate, future spec. Until that spec
   ships, online fee payment remains broken in production; this doc doesn't
   change that fact, it just doesn't try to solve it here.
2. **No idempotency, no audit trail (this spec's scope):** the manual/offline
   payment path (`FeeService.CreatePaymentAsync` → `FeeRepository.CreateAsync`)
   has no duplicate-payment protection, and no action anywhere in the backend
   writes an audit record — there is no `AuditLog` table at all.

This spec fixes problem 2: idempotency for offline/manual fee payments, and
a minimal, reusable audit log — needed independently of any payment gateway,
and required for other modules to adopt later.

**Out of scope:** Razorpay/online fee payment (separate future spec),
refunds (no refund endpoint exists yet for student fees — building one is a
new feature, not a hardening fix), fee reminders/reports, and every
non-Fees module. Audit logging is introduced here as a minimal, reusable
mechanism — Fees is its first and only consumer in this spec; other modules
adopt it in their own future specs.

## 1. Idempotency for offline/manual payments

The manual/staff-recorded payment path (cash, cheque, UPI-manual) has no
duplicate-payment protection: `FeeService.CreatePaymentAsync` →
`FeeRepository.CreateAsync` inserts unconditionally.

Add a client-generated `IdempotencyKey` (GUID) to `FeePayments`, generated
once per form open by `sms-admin`'s payment form (not regenerated on
retry/double-click). Unique index on `(TenantId, IdempotencyKey)`.
`CreateAsync` becomes insert-or-return-existing: on a unique-constraint
violation, it re-reads and returns the existing row instead of erroring or
inserting a duplicate.

This mapping (constraint violation → idempotent response) lives in
`FeeRepository`, not the controller, so callers can't bypass it.

The existing invoice-linked path, `FeeInvoiceRepository
.RecordInvoicePaymentAsync`, already prevents double-pay via
`UPDLOCK`/`HOLDLOCK` + balance re-check inside its transaction — that
protection is unchanged and unaffected by this spec.

## 2. Minimal reusable audit log

New table `AuditLogs` (migration `M0173`, the next available number as of
this writing):

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | RLS-filtered like other tenant tables |
| ActorUserId | uniqueidentifier | nullable (system-initiated actions) |
| Action | nvarchar(100) | e.g. `FeePayment.Recorded` |
| Module | nvarchar(50) | e.g. `Fees` |
| EntityType | nvarchar(100) | e.g. `FeePayment` |
| EntityId | nvarchar(100) | |
| TimestampUtc | datetime2 | default `SYSUTCDATETIME()` |
| BeforeData | nvarchar(max) | JSON, nullable |
| AfterData | nvarchar(max) | JSON |

No update/delete path is exposed through the application — rows are
insert-only, written by a new `IAuditLogger.LogAsync(...)` helper in
`Sms.Shared.Kernel`, designed generically (any module, any action/entity)
so other modules can call it later without schema changes.

Both `FeeRepository.CreateAsync` (manual payments) and
`FeeInvoiceRepository.RecordInvoicePaymentAsync` (invoice-linked payments)
write their audit row **inside the same transaction** as the payment/invoice
write — a rollback removes both, a commit keeps both. The idempotent-replay
case (constraint violation → return existing row) does **not** write a new
audit row, since no new business event occurred. No app-level CRUD can edit
or delete an `AuditLogs` row.

## 3. Schema fixes

Migration adds to `FeePayments`: `CreatedAt datetime2 default
SYSUTCDATETIME()`, `UpdatedAt datetime2 null`, `IdempotencyKey
uniqueidentifier null` + unique filtered index on `(TenantId,
IdempotencyKey)`.

## 4. Tests

Extend `tests/Sms.Tests.Integration/Finance/FeesTests.cs`:

- Duplicate manual payment submission (same `IdempotencyKey`) returns the
  original payment, does not create a second row, does not write a second
  audit row.
- A fresh manual payment writes exactly one `AuditLogs` row with correct
  `TenantId`/`Action`/`EntityId`/`Module`.
- An invoice-linked payment via `RecordInvoicePaymentAsync` writes exactly
  one `AuditLogs` row in the same transaction; a forced failure after the
  payment insert (simulated) rolls back both the payment and the audit row.

## Frontend impact

`sms-admin`'s manual payment recording form needs to generate an
`IdempotencyKey` (GUID) once per form open and include it in the
`POST /fees/payments` body — a small, additive change, not covered by this
backend-focused spec but noted here so it isn't missed during
implementation planning.

Razorpay-calling code in `feePayments.ts` is unaffected by this spec and
will continue to 404 until the separate Razorpay spec ships.
