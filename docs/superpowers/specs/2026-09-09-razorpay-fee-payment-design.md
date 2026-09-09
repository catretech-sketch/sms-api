# Razorpay Online Fee Payment — Design

**Date:** 2026-09-09
**Status:** Draft
**Scope:** `sms-backend` (primary), `sms-admin` (staff-initiated flow + Owner settings), `sms-student` (parent self-serve flow)

## Summary

Parents (and, separately, school staff) can already attempt to pay a fee
invoice "online" today, but it doesn't work: `PayInvoiceAsync`'s no-amount
branch calls a `StubPaymentGateway` that fakes instant success with no real
money movement, and `sms-admin`'s already-built `feePayments.ts` calls
`POST /fees/invoices/{id}/razorpay/order` / `.../razorpay/verify`, which
don't exist on the backend (404 today). This is exactly the gap the
2026-09-04 `fee-payment-production-hardening-design.md` spec flagged and
explicitly deferred ("real Razorpay integration for fee invoices is tracked
as a separate, future spec").

This spec is that spec. It wires real Razorpay payment collection into the
existing fee/invoice flow, **per school** (each school's fee payments settle
into that school's own Razorpay account — the platform never custodies fee
money), reusing as much of the existing, already-proven machinery as
possible:

- The existing `IRazorpayGateway`/`RazorpayGateway` HTTP/HMAC implementation
  (currently used only for Catre's own platform-billing, a single global
  account) — refactored to accept per-tenant credentials instead of only
  the global platform ones, with zero change to the existing platform-billing
  caller.
- The existing `RecordInvoicePaymentAsync` idempotency machinery
  (`IdempotencyKey` unique index + `UPDLOCK`/`HOLDLOCK` balance re-check) —
  a Razorpay payment is just another `FeePayments` row with
  `Method = "Razorpay"` and `IdempotencyKey = razorpay_payment_id`.
- The existing payment-success notification (`NotifyGuardianOnPaymentBestEffortAsync`,
  shipped in the fee-invoice-notifications branch, `phase-0-foundation`
  commit `0b68782`) — fires automatically, unchanged, because it's keyed
  off "a genuinely new row landed in `FeePayments`," not off which payment
  method produced it. No second notification system is built.
- The existing dual staff-or-linked-parent authorization guard already used
  by `PayInvoice` (`RoleChecks.IsStaff(User) || sis.IsLinkedToCallerAsync(...)`).

**Non-goals:** refunds (no refund endpoint exists for student fees at all,
building one is a new feature); partial online payments (online payment is
always for the full remaining balance — partial/negotiated amounts stay a
manual/cash-only capability); Razorpay Route/marketplace sub-accounts;
Email/SMS integration backend (this spec implements only the `razorpay`
slice of the `GET/PUT /school/integrations` endpoint that `sms-admin`
already expects — Email/SMS settings on that same endpoint are a separate
future spec); an order-expiry/cleanup background job for abandoned orders
(the orphaned `FeePaymentOrders` row is harmless bookkeeping, cleaning it up
is a future enhancement).

## 1. Data model

New migration (next available number after the last committed migration,
`M0179`), following existing FluentMigrator conventions (`[Migration(N,
"description")]`, idempotent `Up()` guards, explicit `Down()`):

**`dbo.TenantPaymentCredentials`**

| Column | Type | Notes |
|---|---|---|
| TenantId | uniqueidentifier | PK/FK, one row per tenant per provider |
| Provider | nvarchar(20) | `'razorpay'` (future-proofs for another gateway without redesign; only Razorpay is implemented now) |
| KeyId | nvarchar(100) | plaintext — not secret, Razorpay's own public identifier |
| KeySecretEncrypted | nvarchar(max) | via ASP.NET Core Data Protection, null until configured |
| WebhookSecretEncrypted | nvarchar(max) | via Data Protection, null until configured |
| Mode | nvarchar(10) | `'test'` \| `'live'`, matches `SchoolRazorpaySettings.mode` already in `sms-admin`'s types |
| IsEnabled | bit | Owner can configure but temporarily disable without deleting |
| CreatedAt / UpdatedAt | datetime2 | |

**`dbo.FeePaymentOrders`**

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| InvoiceId | uniqueidentifier | FK -> `FeeInvoices` |
| RazorpayOrderId | nvarchar(100) | unique |
| AmountPaise | int | server-computed remaining balance at creation time |
| Status | nvarchar(20) | `Created` / `Captured` / `Failed` — pure bookkeeping, see §4 |
| InitiatedBy | nvarchar(20) | `'parent'` \| `'staff'` — drives whether a `pay_link` is generated, see §2 |
| CreatedAt / UpdatedAt | datetime2 | |

This table is deliberately **not** a second source of truth for whether an
invoice is paid — `FeePayments` remains that single source of truth, exactly
as today. `FeePaymentOrders` only maps "which order belongs to which invoice
and tenant" so the confirm/webhook handlers know where to look.

**`FeatureCatalog.OnlineFeePayment`** — new tier-gated entry, checked the
same way `FeatureCatalog.AttendanceGeofence` is checked today.

No schema changes to `FeeInvoices`/`FeePayments`.

## 2. Backend components & API surface

- **`ITenantPaymentCredentialService`** (new, `Sms.Application.Services.Finance`
  or a new `Sms.Application.Services.Payments` namespace) —
  `GetActiveAsync(tenantId)` returns decrypted Key Id/Secret/webhook
  secret + mode, or null if unconfigured/disabled; `UpsertAsync(tenantId,
  req)` encrypts and stores (Owner-only, enforced at the controller).
- **Razorpay client refactor** — extract `RazorpayGateway`'s HTTP/HMAC logic
  (`CreateOrderAsync`, `VerifyPaymentSignature`, `VerifyWebhookSignature`)
  into a stateless client taking `(keyId, keySecret)` / `(webhookSecret)` as
  call parameters instead of reading `IOptions<RazorpayOptions>` internally.
  `PlanUpgradeService` keeps calling it with the existing global platform
  options — **zero behavior change for Catre billing**. A new
  `FeeOnlinePaymentService` calls it with each tenant's own decrypted
  credentials.
- **New `FeeController` actions** (same controller, reusing its existing
  `ISisService sis` dependency and the exact `PayInvoice` authorization guard):
  - `POST /fees/invoices/{id}/razorpay/order` — guard:
    `RoleChecks.IsStaff(User) || await sis.IsLinkedToCallerAsync(inv.StudentId, ct)`.
    Recomputes `remaining = inv.Amount - inv.PaidAmount` server-side (the
    request body carries no amount — rule: never trust a client-supplied
    amount). Checks `FeatureCatalog.OnlineFeePayment` entitlement and that
    `TenantPaymentCredentials` is configured+enabled; 403/409 with a clear
    error otherwise, same status-code conventions as the existing manual
    path (409 if the invoice is already fully paid). Converts `remaining`
    (a decimal rupee amount, matching `FeeInvoices.Amount`'s existing unit)
    to paise (`(int)(remaining * 100)`) for both the stored
    `FeePaymentOrders.AmountPaise` and the Razorpay order request — Razorpay's
    API and the existing `IRazorpayGateway.CreateOrderAsync(amountPaise, ...)`
    both operate in paise, matching the Catre-billing caller's existing
    convention. Creates the `FeePaymentOrders` row (`InitiatedBy = "staff"`
    if `RoleChecks.IsStaff`, else `"parent"`) and a real Razorpay order via
    the refactored client. Response: `{order_id, amount, currency, key_id,
    pay_link}` — `amount` is echoed back in paise (matching the existing
    `sms-admin` `FeeRazorpayOrder.amount` contract, which the frontend
    already expects and divides by 100 for display); `pay_link`
    populated (via Razorpay's Payment Links API) only when `InitiatedBy ==
    "staff"`, so front-desk staff can share a link out-of-band; omitted for
    the parent self-serve flow, which opens Razorpay Checkout directly in
    `sms-student`.
  - `POST /fees/invoices/{id}/razorpay/verify` — same guard. Body:
    `{razorpay_order_id, razorpay_payment_id, razorpay_signature}`. Looks up
    the `FeePaymentOrders` row by `razorpay_order_id` (must match this
    tenant + invoice), verifies the signature with this tenant's Key Secret,
    then calls `RecordInvoicePaymentAsync(invoiceId, amount:
    order.AmountPaise / 100m, method: "Razorpay", reference:
    razorpay_payment_id, idempotencyKey: razorpay_payment_id, ct)` — the
    stored `AmountPaise` (not anything from the request body) is the amount
    passed through, converted back to the decimal rupee unit
    `RecordInvoicePaymentAsync` already expects. Marks the `FeePaymentOrders`
    row `Captured`.
    Returns the resulting `FeePayment` (same shape `PayInvoice` already
    returns).
- **New `RazorpayFeeWebhookController`** — `[Route("v1/webhooks")]`,
  `[AllowAnonymous]`, `POST razorpay-fees` (a distinct route from the
  existing platform-billing `POST razorpay` webhook, since these verify
  against different secrets and update different tables). Parses `order_id`
  from the payload (untrusted lookup key only) to find the `FeePaymentOrders`
  row and its tenant, verifies the raw body's signature against *that
  tenant's* webhook secret, and on `payment.captured`/`order.paid` calls the
  same `RecordInvoicePaymentAsync(...)` with the same idempotency key as the
  verify path would use, then marks the order `Captured`. On
  `payment.failed`, marks the order `Failed` and does nothing else. A failed
  signature check returns 400 and touches no state.
- **`FeeController` (Owner-only slice of `/school/integrations`)** — a new,
  small `SchoolIntegrationsController`:
  - `GET /school/integrations` — returns `{razorpay: {enabled, key_id,
    mode, status, key_secret_set, webhook_secret_set}}` for the `razorpay`
    key; `email`/`sms` keys are returned as empty/default placeholders in
    this spec (their backend is a separate future spec — see Non-goals).
  - `PUT /school/integrations` — Owner-only (`[Authorize(Policy =
    Policies.Owner)]`, matching the existing `Policies.Principal` pattern
    already used elsewhere in `FeeController`). Accepts a partial body;
    only the `razorpay` key is persisted by this spec. `key_secret`/
    `webhook_secret` are write-only — a request that omits them leaves the
    stored encrypted value unchanged; sending an empty string clears it.
  - `POST /school/integrations/razorpay/verify` — Owner-only. Makes a
    minimal authenticated call to Razorpay (list orders with `count=1`) using
    the currently-stored credentials to confirm they're valid, updates and
    returns `{status: 'configured' | 'invalid'}` (never `'not_configured'`
    from this action — that's the GET's response when no credentials are
    stored at all).

## 3. Parent authorization & amount integrity

Every new endpoint reuses the *exact* existing guard from `PayInvoice`
(`RoleChecks.IsStaff(User) || await sis.IsLinkedToCallerAsync(inv.StudentId,
ct)`), so a parent can only create/verify orders for a child linked to them,
and staff can act on any student in their own tenant (enforced upstream by
`ITenantContext`/`TenantResolutionMiddleware` on every repository call, same
as every other Fees endpoint today). The amount is always computed
server-side from the invoice's current `Amount - PaidAmount` at order-
creation time and is never accepted from the request body — the client only
ever *echoes back* the number the server already decided.

## 4. Payment state transitions & idempotency

`FeePaymentOrders.Status` (`Created` -> `Captured`/`Failed`) exists purely so
the verify/webhook handlers know which invoice an incoming `order_id`
belongs to — it is not a second source of truth for whether the invoice is
paid. The actual payment record is written only through the existing
`RecordInvoicePaymentAsync`, keyed by `IdempotencyKey = razorpay_payment_id`.
That method already (from the fee-invoice-notifications work): rejects a
*different* invoice/amount under a reused key (`IdempotencyKeyConflictException`),
but returns the existing row silently for a genuine replay of the *same*
key — exactly what happens when the client `verify` call and the webhook
race to report the same `payment_id`. Because guardian notification only
fires when `RecordInvoicePaymentAsync` reports a *genuinely new* row (the
existing Task 3 guard), a duplicate verify/webhook is automatically a no-op
for both payment recording and notification — no new idempotency code is
needed for that half of the guarantee.

An abandoned/cancelled Razorpay Checkout never calls `verify`, and never
produces a `payment.captured`/`order.paid` webhook event — so no
`FeePayments` row is ever created and no notification ever fires for it.
This is true by construction, not by an explicit "was this cancelled" check.

**Webhook signature chicken-and-egg:** each school has its own Razorpay
webhook secret, so the handler cannot know which secret to verify against
until it knows which tenant the webhook is for. It resolves this by reading
`order_id` from the payload *only* as an untrusted lookup key into
`FeePaymentOrders` to find a candidate tenant, then verifies the **full raw
request body's** HMAC signature against that tenant's stored webhook secret
before taking any action. If verification fails, the handler returns 400 and
makes no database change — the untrusted lookup is never used for anything
except picking which secret to try.

Receipt/PDF: the existing Task 4 attachment logic (real invoice PDF +
school logo, attached regardless of `Method`) applies unchanged — a
Razorpay payment gets the identical receipt email a cash payment gets
today.

## 5. Security

- `KeySecret`/`WebhookSecret` are encrypted at rest via ASP.NET Core's Data
  Protection API (`IDataProtector`, ships with .NET — no new infrastructure
  dependency), decrypted only in-process when calling Razorpay or verifying
  a signature. Never logged. Never returned by any GET (`key_secret_set`/
  `webhook_secret_set` booleans only, matching `sms-admin`'s existing type
  contract).
- Both signature checks (`verify` and the webhook) reuse the *existing*
  `VerifyPaymentSignature`/`VerifyWebhookSignature` logic verbatim (already
  constant-time via `CryptographicOperations.FixedTimeEquals`), just
  parameterized with the tenant's own secret instead of the global platform
  one.
- The webhook route stays `[AllowAnonymous]` (Razorpay cannot authenticate
  as a school user) but is fully signature-gated per §4.
- Owner-only RBAC on all credential-management endpoints
  (`Policies.Owner`), matching the explicit product decision that only the
  Owner role — not Principal, not Admin — can configure a school's Razorpay
  keys.

## 6. Feature/tier gating

`FeatureCatalog.OnlineFeePayment` gates `POST .../razorpay/order` (403 if
the school's plan doesn't include it) and drives whether "Pay Online"/
"Collect Online" is shown at all in `sms-student`/`sms-admin`.
Independently, `TenantPaymentCredentials.IsEnabled` (and credentials
actually being configured) gates it too — a tier-entitled school that
hasn't yet had its Owner configure Razorpay simply doesn't get the option.
Manual/cash payment (today's flow) remains the always-available fallback
regardless of tier or configuration state.

## 7. Failure/retry handling

- Razorpay order-creation API error (network, invalid credentials, Razorpay
  outage) -> surfaced to the caller as an error; no `FeePaymentOrders` row
  is left in an ambiguous state (only created after the Razorpay API call
  itself succeeds).
- Parent/staff abandons Checkout mid-flow -> no `verify` call, and normally
  no webhook event either -> invoice stays `due`, nothing to reconcile. If
  Razorpay does eventually send a `payment.failed` event for it, the order
  is marked `Failed` and nothing else happens.
- `verify` never reaches the backend (app crash/connectivity loss right
  after the parent finishes paying in Checkout) -> the **webhook is the
  safety net**: Razorpay retries webhook delivery on failure, and the
  handler is idempotent, so the payment is still recorded and the guardian
  is still notified even without a successful `verify` call. This is the
  reason both paths exist rather than trusting the client alone.

## 8. Test matrix

**Unit tests**
- Signature verification: valid, tampered, wrong-secret (both the
  client-verify shape and the webhook shape).
- Server-side amount computation ignores any amount the client might send.
- Credential encrypt/decrypt round-trip via the Data Protection API.
- Feature-gate and credentials-configured checks (entitled+configured,
  entitled+unconfigured, not-entitled).

**Integration tests** (real HTTP pipeline against the test DB, same style as
the existing `PayInvoiceNotifyTests`)
1. Happy path (parent-initiated): order -> verify -> invoice marked
   paid -> exactly one guardian notification -> PDF receipt attached,
   identical to a cash payment's notification/receipt today.
2. Happy path (staff-initiated via `sms-admin`): same, plus a `pay_link`
   is present in the order response.
3. Duplicate `verify` call with the same `payment_id` twice -> exactly one
   `FeePayments` row, exactly one notification.
4. Webhook arrives after `verify` already processed the same payment, and
   the reverse (webhook-only, `verify` never called) -> both produce
   exactly one payment/notification, no duplicates either way.
5. Cross-tenant / cross-student isolation: a parent cannot create or verify
   an order for a child not linked to them, or for a student in a different
   tenant -> 403 in both cases.
6. Tampered `verify` signature -> rejected, no `FeePayments` row created.
7. Wrong-tenant or tampered webhook signature -> rejected, no state change
   anywhere.
8. Race: invoice already fully paid by the time `order` is called -> 409,
   no `FeePaymentOrders` row created.
9. Tier not entitled -> `order` returns 403. Credentials not configured (or
   disabled) -> `order` returns a clear "not available" error, not a 500.
10. Multi-child parent: creating/verifying an order for child B never
    touches child A's invoices or triggers a notification about child A
    (exercises the existing `IsLinkedToCallerAsync` guard in this new
    context).
11. `GET/PUT /school/integrations` + `POST .../razorpay/verify`: Owner can
    configure and test credentials; a non-Owner role gets 403; secrets are
    never echoed back on GET; omitting `key_secret`/`webhook_secret` on PUT
    leaves the stored value unchanged; sending an empty string clears it.

**End-to-end acceptance tests** — one test per requirement rule below,
run against the real pipeline, explicitly labeled so they're traceable
back to this list:

| # | Rule | Covered by |
|---|---|---|
| 1 | Parent can pay only for a linked child | Integration test 5, 10 |
| 2 | Client-supplied amount is never trusted | Unit test (amount computation), Integration test 1 |
| 3 | Backend/database remains source of truth | §4 design + Integration tests 3, 4 |
| 4 | Failed/cancelled/abandoned payments never notify | §4 design (true by construction) + Integration test 7 |
| 5 | Razorpay callback and webhook are idempotent | Integration tests 3, 4 |
| 6 | Duplicate callbacks/webhooks never create duplicate payments | Integration tests 3, 4 |
| 7 | Duplicate processing never sends duplicate notifications | Integration tests 3, 4 |
| 8 | Reuses the existing payment-success notification | §4 design (zero new notification code) |
| 9 | No second notification system for Razorpay | §4 design |
| 10 | Multi-child parent selects the correct child first | Integration test 10 (backend guard; child-selection UI itself is existing `sms-student` functionality, not new) |
| 11 | Tenant isolation + RBAC enforced throughout | Integration tests 5, 6, 7, 11 |
| 12 | This table | this table |

## Frontend impact (noted, not covered by this backend-focused spec)

- `sms-admin`'s `feePayments.ts`/`schoolIntegrations.ts` and their tests
  already exist and already match the contracts above — no frontend changes
  should be needed there once the backend ships, beyond wiring up a Razorpay
  Checkout call for the staff-initiated in-person flow (if staff pay via
  card-present rather than sharing `pay_link`).
- `sms-student`'s `ParentFeesScreen`/`useFees.ts` currently POSTs an empty
  body to the old stub-gateway `pay` endpoint and needs updating to the new
  order-create -> Razorpay Checkout SDK -> verify flow described in §2.
  This is real, non-trivial mobile-app work and is planned as part of the
  implementation plan, not assumed away.
