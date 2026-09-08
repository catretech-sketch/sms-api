# Fee Structure Publish + Payment-Success Notifications — Design Spec

**Branch:** `feat/fee-invoice-notifications` (based on `phase-0-foundation`)
**Date:** 2026-09-08

## Context

An earlier iteration of this branch fired a parent notification (email + in-app)
when a fee invoice was **generated**. The correct business rule, confirmed with
the product owner, is:

- Fee structure **save** (no status change to active) → no notification.
- Fee structure **first activation** (draft/inactive → active) → notify parents
  a new fee structure is in effect.
- Invoice **generation** → invoice becomes visible to the parent in the existing
  Parent App invoice screens; no "paid" notification.
- Invoice **payment** (offline/cash or the existing online/stub path — both go
  through the same endpoint today) → notify the paying student's guardian only,
  by email and in-app, with an attached invoice PDF.

This spec covers only that behavior. A separate, much larger piece of work —
building a real Razorpay-for-fees integration (tenant-scoped credential
storage, order/verify endpoints, webhook, signature verification) — is
explicitly **out of scope**. Investigation (see Appendix A) confirmed no such
integration exists anywhere in this codebase today, on any branch; the
Razorpay fields visible in the frontend Settings screen have no backend behind
them, and the only Razorpay code in the repository (`IRazorpayGateway`,
`RazorpayGateway`, `PlanUpgradeController`) belongs to an unrelated feature
(Catre billing schools for their own SaaS subscription, not schools collecting
fees from parents). That gap is unaffected by this spec: offline payment does
not depend on Razorpay today and will continue not to.

## Goals

1. Fee structure activation notifies parents tenant-wide (once per genuine
   activation, not on every subsequent edit while already active).
2. Remove the invoice-generation-time notification added earlier on this
   branch.
3. Fire the payment notification only when `PayInvoiceAsync` →
   `RecordInvoicePaymentAsync` creates a genuinely new `FeePayment` row —
   never on an idempotent replay or an already-fully-paid conflict.
4. Attach a real invoice PDF (line items, school logo when configured) to the
   payment-success email, replacing the generic Catre notice PDF for this
   notification only.
5. Reuse 100% of the existing announcement/email/notification/idempotency
   infrastructure. No new tables, no new services duplicating existing ones.

## Non-goals

- Real Razorpay-for-fees integration (separate follow-up project).
- Invoice-generation-time email or PDF (doesn't exist today; not being added —
  Parent App visibility already covers this).
- Any change to the Parent App invoice screens (already correctly scoped to
  the caller's linked child via `DenyUnscopedOrUnlinkedAsync` /
  `IsLinkedToCallerAsync` — confirmed working, not touched).
- Per-class fee-structure scoping (the data model is one active row per
  tenant; not changing that here).

## Design

### 1. Fee structure activation notification

`FeeService.UpsertStructureAsync` currently accepts `Status` of `"active"` or
`"inactive"` and upserts a single row per tenant (`FeeStructureRepository`,
via `dbo.FeeStructure_Upsert`, which updates the tenant's one active row in
place or inserts if none exists).

Change: before upserting, read the tenant's current structure via
`structures.GetAsync(ct)` and capture its previous `Status`. After the upsert
succeeds, if the **new** status is `"active"` and the **previous** status was
NOT `"active"` (i.e. there was no row yet, or it was `"inactive"`), fire a
best-effort notification:

- `audience: "parents"` (the existing, correctly-scoped audience key — it
  resolves to every student + parent-role user in the tenant, which is
  accurate here since the fee structure genuinely applies tenant-wide; no
  bug to work around, unlike the `"specific"` key fixed earlier this
  session).
- `channels: ["app"]` only — no email for this event (matches the agreed
  event matrix: publish is in-app only).
- Title: `"Fee Structure Published"`.
- Body: references the academic year from the request (e.g. `"A new fee
  structure for {academicYear} has been published. Please check the updated
  fee details in the Parent App."`).

Saving while already active (no status transition), or saving as
`"inactive"`, sends nothing. This is a plain conditional in
`UpsertStructureAsync` — no new column, no new migration.

### 2. Remove invoice-generation notification

`FeeService.GenerateInvoicesAsync`: delete the
`NotifyGuardianBestEffortAsync` call and the method itself (it moves, in
spirit, to the payment path — see below). No behavior change to invoice
creation, roster matching, or the `GenerateFeeInvoicesResponse` shape.

### 3. Payment-success notification

`FeeService.PayInvoiceAsync` already has a single, clear point where a
genuinely new payment is known to exist: where `RecordInvoicePaymentAsync`
returns a non-null `FeePaymentResponse` (as opposed to the three earlier
return paths — idempotency-key hit, existing-payment race caught via unique
constraint, or the null returned when the invoice was already fully paid).
Immediately after that point, and before the method's existing
`live.PublishAsync(...)` call, add a best-effort notify:

- Resolve the invoice's student via the existing roster lookup already
  available in this method (`inv.StudentId` → student record) to get
  `GuardianEmail`/`GuardianPhone`.
- `audience: "specific"`, explicit `Emails`/`Phones` from the guardian
  fields, `UserId` resolved via `IAuthDao.GetByEmailAndTenantAsync` (the
  same pattern already used and tested for the generate-time notifier,
  moved here).
- `channels: ["email", "app"]`.
- Title: `"Invoice Paid"`.
- Body built from the real payment: student name, invoice period, amount
  paid (`payment.Amount`), payment method (`payment.Method`), payment date.
  Example: `"Invoice for {period} for {studentName} has been paid
  successfully. Amount received: ₹{amount}. Payment method: {method}."`
- The call is wrapped in try/catch exactly like the existing pattern —
  failure is logged, never surfaced to the API caller, never rolls back the
  payment already committed by `RecordInvoicePaymentAsync`.

Because this fires only on the one code path that represents "a new
`FeePayment` row was actually created," idempotent replay and already-paid
conflicts are automatically excluded — no new duplicate-tracking mechanism is
needed; the existing idempotency-key and unique-constraint handling already
in `RecordInvoicePaymentAsync` is the sole source of truth.

Both today's "offline/cash" path (`req.Amount` present) and the "online/stub
gateway" path (`req.Amount` omitted, `gateway.ChargeAsync` called) converge on
the same `RecordInvoicePaymentAsync` call, so both get this notification
identically with no separate code path.

### 4. Invoice PDF with school logo

New `IFeeInvoicePdfGenerator` / `FeeInvoicePdfGenerator` in
`Sms.Application/Services/Finance` (mirrors `NoticePdfGenerator`'s use of
QuestPDF, but a distinct document — the existing generator's model has no
image support and a fixed generic-notice layout unsuited to line items).

```csharp
public sealed record FeeInvoicePdfModel(
    string SchoolName, string? LogoUrl, string StudentName, string Period,
    decimal Amount, decimal PaidAmount, decimal DueAmount, string Status,
    string PaymentMethod, DateTime PaymentDate);
```

Layout: header with school name + logo image (via QuestPDF's `Image()`,
fetched from `LogoUrl` when set — best-effort, blank header block if the
fetch fails or `LogoUrl` is null), a simple line-item/summary table (period,
total, paid, due, status), payment method and date. No new dependency —
QuestPDF is already referenced by `Sms.Application`.

`FeeService.NotifyGuardianOnPaymentBestEffortAsync` builds this model from
the real invoice/payment/student data plus `ClientRepository.GetAsync(tid,
ct)`'s `LogoUrl`, renders it, and passes the bytes via
`CreateAnnouncementRequest.AttachmentBase64` /`AttachmentFileName` (fields
that already exist on the record and are already wired through
`AnnouncementService.CreateAsync`, just unused by any caller today).

**Small `AnnouncementService` change:** currently it unconditionally
generates and attaches the generic `NoticePdfGenerator` notice PDF whenever
`channels` includes `"email"`, regardless of whether the caller also supplied
`AttachmentBase64`. Guard that generation on `req.AttachmentBase64 is null` —
when a caller supplies its own attachment, skip the generic notice PDF
instead of sending two PDFs in one email. This does not change behavior for
any existing caller (none currently supplies `AttachmentBase64`).

## Testing

All in `Sms.Tests.Integration`, following the existing
`WebApplicationFactory<Program>` + `SqlServerFixture` pattern used elsewhere
on this branch (real HTTP pipeline, real SQL Server, a capturing
`IAnnouncementService` test double where the assertion is about the
notification, real end-to-end HTTP calls where the assertion is about the
notification NOT firing or about duplicate-protection).

- Fee structure: save with no status change → 0 notify calls. First
  activation (no prior row, or prior row inactive) → 1 notify call,
  `channels: ["app"]` only, audience `"parents"`. Re-saving while already
  active → 0 additional calls.
- Invoice generation: 0 notify calls (the removed call's absence, asserted
  via the capturing fake never being invoked across a multi-student
  generate).
- Payment: new cash payment → 1 notify call with correct guardian
  email/phone/UserId and a body containing the real amount/method/period.
  New online/stub payment → same, identical shape. Idempotent-key replay →
  0 additional calls. Already-fully-paid conflict (409) → 0 calls. Notify
  service throwing → payment still recorded, API still returns success.
- Recipient/tenant isolation: two students in the same tenant — paying
  Student A's invoice never includes Student B's guardian in the call; two
  tenants — paying an invoice in Tenant A never resolves or notifies a
  Tenant B user (existing `IAuthDao.GetByEmailAndTenantAsync`'s tenant
  parameter already enforces this, tested directly).
- PDF: a rendered `FeeInvoicePdfModel` produces non-empty PDF bytes; a
  payment notification's `AttachmentBase64`/`AttachmentFileName` are set; a
  generic-notice-PDF-suppression test confirms `AnnouncementService` attaches
  only one PDF when `AttachmentBase64` is supplied.

## Appendix A: Razorpay investigation (informs Non-goals)

Grepped for `razorpay/order`, `razorpay/verify`, `RazorpayOrder`,
`FeeRazorpay`, `school/integrations`, `SchoolIntegrations`,
`RazorpaySettings` across every branch/worktree in the sms-backend repo,
including `main`. The only matches are `IRazorpayGateway`/`RazorpayGateway`/
`PlanUpgradeController`/`PlanUpgradeService` — all part of the platform
subscription plan-upgrade feature (Catre charging schools), not fee
collection. No table, service, or controller for per-tenant Razorpay
settings exists anywhere. The frontend Settings screen's Razorpay fields
call `/v1/school/integrations`, which 404s on every branch checked. Building
real Razorpay-for-fees requires: a new per-tenant encrypted-credential
store + CRUD API, new order-creation and payment-verification endpoints
(reusing `IRazorpayGateway`'s existing HTTP-client plumbing where
applicable), webhook signature verification, and wiring into
`RecordInvoicePaymentAsync`. That is a distinct, follow-up project.
