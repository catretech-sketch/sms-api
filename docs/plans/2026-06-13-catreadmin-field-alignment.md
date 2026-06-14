# sms-catreadmin — Canonical Field Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a canonical data-access seam to the `sms-catreadmin` prototype (which has no HTTP layer — it reads `data.jsx` directly) so the Catre super-admin contract is expressed once in canonical snake_case, ready for a real API, without rewriting the UI.

**Architecture:** Add three plain-JS files under `api/`: `contracts.js` (canonical key lists per entity), `adapter.js` (pure CommonJS functions mapping `data.jsx` mock objects → canonical DTOs), and `adapter.test.js` (a Node `assert` script — the prototype has no test framework). The UI keeps reading `data.jsx` for now; the adapter is the forward-compatible seam the future backend response shape must match.

**Tech Stack:** Browser CDN React prototype (no build, no package.json). Adapter + test are plain CommonJS runnable with `node`.

**Canonical reference:** §3B "Catre-only entities" + "Tenant" in `2026-06-13-backend-api-design.md`.

**Entities covered (source fields verified in `data.jsx`):** Tenant (from `CLIENTS`), Plan (`PLANS`), TeamMember (`TEAM`), Invoice (`INVOICES`), SupportTicket (`TICKETS`).

---

### Task 1: Branch

**Files:** none

- [ ] **Step 1: Create branch**

```bash
cd /d/SMS/sms-project/sms-catreadmin
git checkout -b field-alignment-canonical
```

- [ ] **Step 2: Confirm Node is available**

Run: `node --version`
Expected: prints a version (e.g. `v20.x`). The assertion script needs only Node, no install.

---

### Task 2: Canonical contracts + adapter + Node assertion

**Files:**
- Create: `api/contracts.js`
- Create: `api/adapter.js`
- Create: `api/adapter.test.js`

- [ ] **Step 1: Write the failing assertion script**

Create `api/adapter.test.js`:

```js
const assert = require('assert');
const C = require('./contracts');
const A = require('./adapter');

function sameKeys(obj, expected, label) {
  assert.deepStrictEqual(Object.keys(obj).sort(), [...expected].sort(), `${label} keys mismatch`);
}

// ── Tenant (from a CLIENTS entry) ──
const client = {
  id: 'tn_greenwood', name: 'Greenwood High', slug: 'greenwood', status: 'active',
  country: 'Mumbai, MH', plan: 'pl_gold', planName: 'Gold', tier: 'gold', mrr: 14999,
  students: 900, staff: 80, storage: 22.5, limits: { students: 1200, staff: 120, storage_gb: 50 },
  created: '2025-01-10', createdAgo: 500, lastActive: 'today', lastActiveDays: 0, trialEnds: null,
  contact: { name: 'Aarav Sharma', email: 'admin@greenwood.edu.in', phone: '+91 98765 43210' },
  csm: 'Priya Nair', healthScore: 88,
  gateway: { provider: 'Razorpay', method: 'upi_autopay', mandate: 'active', vpa: 'greenwood@okhdfcbank', card: null, bank: null, maxAmount: 20000, mandateId: 'raz_mnd_abcd1234' },
  usageSeries: [800, 810, 820],
};
const tenant = A.toTenantDTO(client);
sameKeys(tenant, C.TENANT_KEYS, 'Tenant');
assert.strictEqual(tenant.plan_name, 'Gold');
assert.strictEqual(tenant.health_score, 88);
assert.strictEqual(tenant.gateway.max_amount, 20000);
assert.strictEqual(tenant.usage_series.length, 3);

// ── Plan ──
const plan = {
  id: 'pl_gold', name: 'Gold', tier: 'gold', pricing: 'flat', price: 14999, perStudent: undefined,
  minStudents: undefined, period: 'month', features: ['sis.students'], limits: { students: 1200, staff: 120, storage_gb: 50 },
  visibility: 'published', audience: 'all', band: '300–1,200 students', offer: { label: 'Annual', pct: 16 },
  color: 'var(--amber)', desc: 'Full operations.',
};
const planDTO = A.toPlanDTO(plan);
sameKeys(planDTO, C.PLAN_KEYS, 'Plan');
assert.strictEqual(planDTO.per_student, null);
assert.strictEqual(planDTO.description, 'Full operations.');

// ── TeamMember ──
const member = { id: 'u1', name: 'Aanya Sharma', email: 'aanya@catre.io', role: 'owner', status: 'active', lastLogin: '2h ago', joined: '2023-01-12' };
const memberDTO = A.toTeamMemberDTO(member);
sameKeys(memberDTO, C.TEAM_MEMBER_KEYS, 'TeamMember');
assert.strictEqual(memberDTO.last_login, '2h ago');

// ── Invoice ──
const invoice = { id: 'INV-10480', client: 'Greenwood High', clientId: 'tn_greenwood', plan: 'Gold', amount: 14999, status: 'paid', issued: '2026-05-01', due: '2026-05-15', paidOn: '2026-05-03' };
const invoiceDTO = A.toInvoiceDTO(invoice);
sameKeys(invoiceDTO, C.INVOICE_KEYS, 'Invoice');
assert.strictEqual(invoiceDTO.tenant_id, 'tn_greenwood');
assert.strictEqual(invoiceDTO.paid_on, '2026-05-03');

// ── SupportTicket ──
const ticket = { id: 'TK-2040', subject: 'Import failing', client: 'Greenwood High', clientId: 'tn_greenwood', status: 'open', priority: 'high', assignee: 'Priya Nair', created: '2026-06-01', updated: '2026-06-02', messages: 4 };
const ticketDTO = A.toTicketDTO(ticket);
sameKeys(ticketDTO, C.SUPPORT_TICKET_KEYS, 'SupportTicket');
assert.strictEqual(ticketDTO.messages_count, 4);

console.log('OK: all canonical adapter contracts pass');
```

- [ ] **Step 2: Run the script to verify it fails**

Run: `node api/adapter.test.js`
Expected: FAIL — `Cannot find module './contracts'` (files do not exist yet).

- [ ] **Step 3: Create `api/contracts.js`**

```js
// Canonical Catre super-admin contract — the snake_case keys each DTO must expose.
// These mirror §3B of the backend API design. The future API response shape must match.

const TENANT_KEYS = [
  'id', 'name', 'slug', 'country', 'status', 'plan_id', 'plan_name', 'tier', 'mrr',
  'students_count', 'staff_count', 'storage_gb', 'limits', 'created', 'last_active_days',
  'trial_ends_days', 'contact', 'csm', 'health_score', 'gateway', 'usage_series',
];

const PLAN_KEYS = [
  'id', 'name', 'tier', 'pricing', 'price', 'per_student', 'min_students', 'period',
  'features', 'limits', 'visibility', 'audience', 'band', 'offer', 'color', 'description',
];

const TEAM_MEMBER_KEYS = ['id', 'name', 'email', 'role', 'status', 'last_login', 'joined'];

const INVOICE_KEYS = ['id', 'tenant_id', 'tenant_name', 'plan_name', 'amount', 'status', 'issued', 'due', 'paid_on'];

const SUPPORT_TICKET_KEYS = ['id', 'subject', 'tenant_id', 'tenant_name', 'status', 'priority', 'assignee', 'created', 'updated', 'messages_count'];

module.exports = { TENANT_KEYS, PLAN_KEYS, TEAM_MEMBER_KEYS, INVOICE_KEYS, SUPPORT_TICKET_KEYS };
```

- [ ] **Step 4: Create `api/adapter.js`**

```js
// Pure adapters: data.jsx mock objects -> canonical snake_case DTOs.
// No browser/React dependency, so they run under Node and in the future API client.

function toTenantDTO(c) {
  return {
    id: c.id,
    name: c.name,
    slug: c.slug,
    country: c.country,
    status: c.status,
    plan_id: c.plan,
    plan_name: c.planName,
    tier: c.tier,
    mrr: c.mrr,
    students_count: c.students,
    staff_count: c.staff,
    storage_gb: c.storage,
    limits: c.limits,
    created: c.created,
    last_active_days: c.lastActiveDays,
    trial_ends_days: c.trialEnds,
    contact: c.contact,
    csm: c.csm,
    health_score: c.healthScore,
    gateway: c.gateway
      ? {
          provider: c.gateway.provider,
          method: c.gateway.method,
          mandate: c.gateway.mandate,
          vpa: c.gateway.vpa,
          card: c.gateway.card,
          bank: c.gateway.bank,
          max_amount: c.gateway.maxAmount,
          mandate_id: c.gateway.mandateId,
        }
      : null,
    usage_series: c.usageSeries,
  };
}

function toPlanDTO(p) {
  return {
    id: p.id,
    name: p.name,
    tier: p.tier,
    pricing: p.pricing,
    price: p.price,
    per_student: p.perStudent == null ? null : p.perStudent,
    min_students: p.minStudents == null ? null : p.minStudents,
    period: p.period,
    features: p.features,
    limits: p.limits,
    visibility: p.visibility,
    audience: p.audience,
    band: p.band,
    offer: p.offer,
    color: p.color,
    description: p.desc,
  };
}

function toTeamMemberDTO(u) {
  return {
    id: u.id,
    name: u.name,
    email: u.email,
    role: u.role,
    status: u.status,
    last_login: u.lastLogin,
    joined: u.joined,
  };
}

function toInvoiceDTO(inv) {
  return {
    id: inv.id,
    tenant_id: inv.clientId,
    tenant_name: inv.client,
    plan_name: inv.plan,
    amount: inv.amount,
    status: inv.status,
    issued: inv.issued,
    due: inv.due,
    paid_on: inv.paidOn,
  };
}

function toTicketDTO(t) {
  return {
    id: t.id,
    subject: t.subject,
    tenant_id: t.clientId,
    tenant_name: t.client,
    status: t.status,
    priority: t.priority,
    assignee: t.assignee,
    created: t.created,
    updated: t.updated,
    messages_count: t.messages,
  };
}

module.exports = { toTenantDTO, toPlanDTO, toTeamMemberDTO, toInvoiceDTO, toTicketDTO };
```

- [ ] **Step 5: Run the script to verify it passes**

Run: `node api/adapter.test.js`
Expected: prints `OK: all canonical adapter contracts pass` and exits 0.

- [ ] **Step 6: Commit**

```bash
git add api/contracts.js api/adapter.js api/adapter.test.js
git commit -m "feat(catreadmin): add canonical contract + data.jsx->DTO adapter seam"
```

---

### Task 3: Document the seam + push

**Files:**
- Create: `api/README.md`

- [ ] **Step 1: Write `api/README.md`**

```md
# Canonical API seam

`adapter.js` maps the prototype's `data.jsx` mock objects into the canonical
snake_case Catre contract defined in `contracts.js` (see
`sms-backend/docs/2026-06-13-backend-api-design.md` §3B).

When the real backend lands, its JSON responses must already match these keys —
the adapters become identity (or move server-side). `node api/adapter.test.js`
guards the contract.

Covered: Tenant, Plan, TeamMember, Invoice, SupportTicket.
Follow-up: Subscription, OnboardingItem, AuditLog (add adapters + keys the same way).
```

- [ ] **Step 2: Re-run the assertion to confirm still green**

Run: `node api/adapter.test.js`
Expected: `OK: all canonical adapter contracts pass`.

- [ ] **Step 3: Commit + push**

```bash
git add api/README.md
git commit -m "docs(catreadmin): document canonical API seam"
git push -u origin field-alignment-canonical
```

---

## Self-Review

**Spec coverage (catreadmin section):** data-access seam introduced ✓ (Task 2: `api/contracts.js` + `api/adapter.js`); canonical DTOs for the Catre superset (Tenant billing fields, Plan, TeamMember, Invoice, SupportTicket) ✓; key-check assertion in place of a test framework ✓ (Task 2 `adapter.test.js`); UI untouched ✓ (adapter is additive). Subscription/OnboardingItem/AuditLog are explicitly listed as same-pattern follow-ups in `api/README.md` (their `data.jsx` source field names should be confirmed before adding, to avoid guessing).

**Placeholder scan:** none — `contracts.js`, `adapter.js`, and `adapter.test.js` are given in full; every run step has an exact command + expected output.

**Type consistency:** every key produced by an adapter function in `adapter.js` is present in the matching `*_KEYS` array in `contracts.js` (Tenant 21 keys, Plan 16, TeamMember 7, Invoice 9, SupportTicket 10); the test's `sameKeys` enforces exact equality, so any drift fails the assertion.
