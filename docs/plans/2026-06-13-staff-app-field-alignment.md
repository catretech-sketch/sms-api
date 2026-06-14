# sms-staff — Canonical Field Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `sms-staff`'s HTTP DTOs fully onto the canonical School-Admin contract — the only divergences are `RouteDTO.assigned_bus_no` (→ `bus_no`), the `LeaveRequestDTO.type` union, and a few missing canonical `StaffDTO` fields.

**Architecture:** Change only `src/data/http/mappers.ts`. Mappers translate canonical DTOs into the existing domain types (`src/data/domain/*`), which are untouched. New canonical mapper tests lock the contract.

**Tech Stack:** TypeScript, React Native (Expo), Jest (`@/` alias), TanStack Query. Own git repo.

**Canonical reference:** §3B in `2026-06-13-backend-api-design.md`.

**Already canonical (verified, no change):** `StaffDTO` (core fields), `TenantDTO` (`logo_url`), `SessionDTO`, `DashboardDTO`, `AttendanceDTO`, `StopDTO` (`seq`,`eta_min`), `TripDTO`, `TripSummaryDTO`, `StudentLiteDTO`, `BoardingDTO`, `TripAssignmentDTO` (top-level `bus_no`), `TaskDTO`, `LeaveBalanceDTO`, `LeaveRequestDTO` dates (`from_date`/`to_date`), `ProfileDTO`.

---

### Task 1: Branch and capture baseline

**Files:** none

- [ ] **Step 1: Create branch**

```bash
cd /d/SMS/sms-project/sms-staff
git checkout -b field-alignment-canonical
```

- [ ] **Step 2: Confirm green baseline**

Run: `npm test`
Expected: all suites PASS.

- [ ] **Step 3: Marker commit**

```bash
git commit --allow-empty -m "chore: start canonical field alignment"
```

---

### Task 2: RouteDTO `assigned_bus_no` → `bus_no`

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `RouteDTO` line 105 and `toRoute` line 116)
- Create: `src/data/http/__tests__/mappers.canonical.test.ts`

- [ ] **Step 1: Write the failing test**

Create `src/data/http/__tests__/mappers.canonical.test.ts`:

```ts
import { toRoute, type RouteDTO } from '@/data/http/mappers';

describe('RouteDTO canonical contract', () => {
  const dto: RouteDTO = {
    id: 'r1',
    name: 'North Loop',
    bus_no: 'WBA-07',
    stops: [{ id: 's1', name: 'Gate 1', lat: 40, lng: -75, seq: 1, eta_min: 5 }],
  };

  it('declares bus_no (not assigned_bus_no)', () => {
    expect(Object.keys(dto)).toContain('bus_no');
    expect(Object.keys(dto)).not.toContain('assigned_bus_no');
  });

  it('maps bus_no to domain Route.assignedBusNo', () => {
    const route = toRoute(dto);
    expect(route.assignedBusNo).toBe('WBA-07');
    expect(route.stops[0]).toEqual({ id: 's1', name: 'Gate 1', lat: 40, lng: -75, seq: 1, etaMin: 5 });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts`
Expected: FAIL — `RouteDTO` still has `assigned_bus_no`, not `bus_no`.

- [ ] **Step 3: Edit `RouteDTO` and `toRoute` in `src/data/http/mappers.ts`**

Replace line 105:

```ts
export interface RouteDTO { id: string; name: string; bus_no: string; stops: StopDTO[]; }
```

Replace line 116 (`toRoute`):

```ts
export const toRoute = (d: RouteDTO): Route => ({ id: d.id, name: d.name, assignedBusNo: d.bus_no, stops: d.stops.map(toStop) });
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(staff): align RouteDTO to canonical bus_no"
```

---

### Task 3: LeaveRequestDTO type → canonical union; StaffDTO → add canonical superset fields

**Files:**
- Modify: `src/data/http/mappers.ts` (`StaffDTO` lines 9–21; `LeaveBalanceDTO`/`LeaveRequestDTO` lines 136–137)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

- [ ] **Step 1: Add the failing test**

```ts
import { toStaff, toLeaveRequest, type StaffDTO, type LeaveRequestDTO, type CanonicalLeaveType } from '@/data/http/mappers';

describe('StaffDTO + LeaveRequestDTO canonical contract', () => {
  it('StaffDTO carries canonical superset fields and maps the core ones', () => {
    const dto: StaffDTO = {
      id: 'u1', name: 'Ravi Kumar', first_name: 'Ravi', role_key: 'driver',
      emp_id: 'E-100', joined: '2025-01-01', rating: 4.5, duty_post: 'Depot Gate',
      shift: 'morning', timing: '06:00-14:00', phone: '9876500000',
      category: 'transport', department: 'Transport', attendance_pct: 96,
      status: 'active', avatar_hue: 30,
    };
    const s = toStaff(dto);
    expect(s.roleKey).toBe('driver');
    expect(s.empId).toBe('E-100');
    expect(s.firstName).toBe('Ravi');
  });

  it('LeaveRequestDTO accepts the canonical leave-type union', () => {
    const types: CanonicalLeaveType[] = ['casual', 'sick', 'earned', 'medical', 'maternity', 'emergency', 'other'];
    expect(types).toContain('earned');
    const dto: LeaveRequestDTO = {
      id: 'l1', type: 'earned', from_date: '2026-07-01', to_date: '2026-07-02',
      reason: 'Trip', status: 'pending',
    };
    expect(toLeaveRequest(dto).fromDate).toBe('2026-07-01');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "canonical contract"`
Expected: FAIL — `StaffDTO` lacks `category`/`department`/`attendance_pct`/`status`/`avatar_hue`; `CanonicalLeaveType` is not exported.

- [ ] **Step 3: Replace `StaffDTO` (lines 9–21) in `src/data/http/mappers.ts`**

```ts
export interface StaffDTO {
  id: string;
  name: string;
  first_name: string;
  role_key: Role;
  emp_id: string;
  joined: string;
  rating: number;
  duty_post: string;
  shift: string;
  timing: string;
  phone: string;
  // canonical school-admin superset (mapper ignores fields the domain does not use)
  category?: string;
  department?: string;
  attendance_pct?: number;
  status?: 'active' | 'inactive';
  avatar_hue?: number;
}
```

- [ ] **Step 4: Add the canonical leave type and widen `LeaveRequestDTO` (lines 136–137)**

Replace lines 136–137 with:

```ts
export type CanonicalLeaveType =
  | 'casual' | 'sick' | 'earned' | 'medical' | 'maternity' | 'emergency' | 'other';
export interface LeaveBalanceDTO { type: CanonicalLeaveType; total: number; used: number; }
export interface LeaveRequestDTO { id: string; type: CanonicalLeaveType; from_date: string; to_date: string; reason: string; status: LeaveRequest['status']; }
```

> `toLeaveRequest` / `toLeaveBalance` (lines 139–140) already read `d.type`, `d.from_date`, `d.to_date` — no change needed because the domain `LeaveRequest['type']` (`casual|sick|earned`) is a subset of the canonical union (assignment is widening-compatible).

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "canonical contract"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(staff): widen leave type union + add canonical StaffDTO superset fields"
```

---

### Task 4: Full-suite regression

**Files:** none

- [ ] **Step 1: Type-check**

Run: `npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 2: Full suite**

Run: `npm test`
Expected: ALL PASS — including existing `src/data/http/__tests__/mappers.test.ts`, `repos.test.ts`, and `src/data/__tests__/contract.test.ts` (domain types unchanged → still green), plus the new canonical tests.

- [ ] **Step 3: Final commit + push**

```bash
git commit --allow-empty -m "test(staff): canonical field alignment verified green"
git push -u origin field-alignment-canonical
```

---

## Self-Review

**Spec coverage (staff section):** `assigned_bus_no`→`bus_no` ✓ (Task 2); leave type union widened ✓ (Task 3); StaffDTO canonical superset fields added ✓ (Task 3); dates already `from_date`/`to_date` (noted, no change); `logo_url` already present (noted). 

**Placeholder scan:** none — full code in every code step, exact commands + expected results in every run step.

**Type consistency:** `RouteDTO`/`toRoute`, `StaffDTO`/`toStaff`, `CanonicalLeaveType`/`LeaveRequestDTO`/`LeaveBalanceDTO`/`toLeaveRequest` all referenced consistently; widening the domain subset union into the canonical union is assignment-safe.
