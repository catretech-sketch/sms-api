# sms-admin — Backend-Ready Implementation Plan (frontend only)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.
>
> **SCOPE GUARD:** This plan modifies ONLY the `sms-admin` frontend (`D:\SMS\sms-project\sms-admin`). It does NOT build, run, or modify the .NET backend. It makes the app *able* to talk to a canonical backend (live or mock) behind one switch.

**Goal:** Make the school-admin CRM actually consume a backend: route all reads/writes through a real `RestApi` (Bearer token + `X-Tenant-Id`, snake_case canonical DTOs, canonical routes), add the missing write methods, and flip between mock and live via an env var — with the UI unchanged.

**Architecture:** Today the `Api`/`MockApi` in `src/lib/api.ts` is **dead code**; `AppProvider` seeds domain arrays from `mockDb.ts` and mutates them locally. We (1) expand `Api` with write methods, (2) build an `HttpApi`/`RestApi` using a fetch client that injects auth+tenant headers and maps snake_case DTOs ↔ the (canonical) domain types in `src/types/index.ts`, (3) keep `MockApi` implementing the same interface, (4) select between them by `import.meta.env.VITE_DATA_SOURCE`, and (5) **rewire `AppProvider`** to load initial data via `api.list*()` and persist via `api.create*()/update*()/delete*()`. UI screens and domain types are untouched.

**Tech Stack:** React 19 + Vite + TS, Vitest (`npm run test` → `vitest run`), `@/` alias. Own git repo.

**Canonical reference:** §3B (fields) + §3C (routes) in `2026-06-13-backend-api-design.md`. `sms-admin/src/types/index.ts` IS the canonical domain model, so DTOs are its snake_case mirror.

**Key facts (from exploration):**
- `src/lib/api.ts`: `Api` has 14 read methods; `MockApi` implemented; `RestApi` is a commented cookie-auth sketch; `export const api = new MockApi()`. No screen imports `api`.
- Writes today: `studentAdd.tsx`/`teacherAdd.tsx`/`staffAdd.tsx` `save()` → `app.addStudent/addTeacher/addStaff` (`AppProvider.tsx` lines ~105/107/109) → `setState` prepend. No update/delete anywhere.
- Auth/tenant: `AppProvider` holds `user`, `consoleKind` ('owner'|'school'), `schoolId` (default `'grv'`), `ownerViewingSchool`. Demo login, **no token**.
- Env: no `.env`, no `VITE_*` in use except the commented sketch's `VITE_API_BASE`.
- Tests: Vitest + jsdom; existing `src/lib/api.test.ts` (MockApi), `AppProvider.test.tsx`, `studentAdd.test.tsx`, etc.

---

### Task 1: Branch + baseline

- [ ] **Step 1:** `cd /d/SMS/sms-project/sms-admin && git checkout -b backend-ready`
- [ ] **Step 2:** Run `npm run test` — confirm green baseline; note the test count.
- [ ] **Step 3:** `git commit --allow-empty -m "chore: start sms-admin backend-ready"`

---

### Task 2: HTTP client with Bearer + X-Tenant-Id + env base URL

**Files:** Create `src/lib/http.ts`; Create `src/lib/http.test.ts`

- [ ] **Step 1: Failing test** — `src/lib/http.test.ts`:

```ts
import { describe, it, expect, vi } from 'vitest';
import { createHttpClient } from '@/lib/http';

describe('http client', () => {
  it('sends Bearer token + X-Tenant-Id and prefixes the base URL', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ ok: true }) });
    const http = createHttpClient({ baseUrl: '/v1', getAuth: () => ({ token: 't1', tenantId: 'grv' }), fetchImpl });
    await http.get('/students');
    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe('/v1/students');
    expect(init.headers.Authorization).toBe('Bearer t1');
    expect(init.headers['X-Tenant-Id']).toBe('grv');
  });

  it('throws on non-2xx with the server message', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({ ok: false, status: 422, json: async () => ({ message: 'bad' }) });
    const http = createHttpClient({ baseUrl: '', getAuth: () => ({ token: null, tenantId: null }), fetchImpl });
    await expect(http.get('/x')).rejects.toThrow('bad');
  });
});
```

- [ ] **Step 2:** `npx vitest run src/lib/http.test.ts` → FAIL (module missing).
- [ ] **Step 3:** Create `src/lib/http.ts`:

```ts
export interface AuthSnapshot { token: string | null; tenantId: string | null; }
export interface HttpConfig {
  baseUrl: string;
  getAuth: () => AuthSnapshot;
  fetchImpl?: typeof fetch;
}
export interface HttpClient {
  get<T>(path: string): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  patch<T>(path: string, body?: unknown): Promise<T>;
  delete<T>(path: string): Promise<T>;
}

export function createHttpClient(cfg: HttpConfig): HttpClient {
  const doFetch = cfg.fetchImpl ?? fetch;
  async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const { token, tenantId } = cfg.getAuth();
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (tenantId) headers['X-Tenant-Id'] = tenantId;
    const res = await doFetch(`${cfg.baseUrl}${path}`, {
      method, headers, body: body == null ? undefined : JSON.stringify(body),
    });
    if (!res.ok) {
      let message = `HTTP ${res.status}`;
      try { const j = await res.json(); if (j?.message) message = j.message; } catch { /* noop */ }
      throw new Error(message);
    }
    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
  }
  return {
    get: (p) => request('GET', p),
    post: (p, b) => request('POST', p, b),
    patch: (p, b) => request('PATCH', p, b),
    delete: (p) => request('DELETE', p),
  };
}
```

- [ ] **Step 4:** `npx vitest run src/lib/http.test.ts` → PASS.
- [ ] **Step 5:** `git add src/lib/http.ts src/lib/http.test.ts && git commit -m "feat(admin): http client with Bearer + X-Tenant-Id"`

---

### Task 3: Canonical DTOs + mappers (worked example: Student)

**Files:** Create `src/lib/dtos.ts`; Create `src/lib/mappers.ts`; Create `src/lib/mappers.test.ts`

The domain types in `src/types/index.ts` are canonical. DTOs are their snake_case mirror; mappers convert. This task implements **Student** fully as the pattern; Task 4's checklist applies the identical pattern to the other entities.

- [ ] **Step 1: Failing test** — `src/lib/mappers.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { toStudent, toStudentDTO } from '@/lib/mappers';
import type { StudentDTO } from '@/lib/dtos';

const dto: StudentDTO = {
  id: 'S1', admission_no: 'ADM-1', name: 'Maya', gender: 'F', grade: 'IX', section: 'A',
  class_label: 'IX-A', roll: 12, guardian: 'Priya', phone: '99999', attendance_pct: 92,
  fee_status: 'paid', fee_due: 0, status: 'active', house: 'Blue', avatar_hue: 210,
};

describe('Student mapper', () => {
  it('maps canonical DTO → domain Student', () => {
    const s = toStudent(dto);
    expect(s.adm).toBe('ADM-1');
    expect(s.cls).toBe('IX-A');
    expect(s.attendance).toBe(92);
    expect(s.feeStatus).toBe('paid');
  });
  it('maps domain Student → canonical DTO (snake_case)', () => {
    const back = toStudentDTO(toStudent(dto));
    expect(back.admission_no).toBe('ADM-1');
    expect(back.attendance_pct).toBe(92);
    expect(back.class_label).toBe('IX-A');
  });
});
```

- [ ] **Step 2:** `npx vitest run src/lib/mappers.test.ts` → FAIL.
- [ ] **Step 3:** Create `src/lib/dtos.ts` (Student shown; add the other entity DTOs in Task 4):

```ts
export interface StudentDTO {
  id: string; admission_no: string; name: string; gender: 'M' | 'F';
  grade: string; section: string; class_label: string; roll: number;
  guardian: string; phone: string; attendance_pct: number;
  fee_status: 'paid' | 'partial' | 'due'; fee_due: number;
  status: 'active' | 'inactive'; house: string; avatar_hue: number;
}
```

- [ ] **Step 4:** Create `src/lib/mappers.ts` (Student shown; add the others in Task 4):

```ts
import type { Student } from '@/types';
import type { StudentDTO } from './dtos';

export const toStudent = (d: StudentDTO): Student => ({
  id: d.id, adm: d.admission_no, name: d.name, gender: d.gender,
  grade: d.grade, section: d.section, cls: d.class_label, roll: d.roll,
  guardian: d.guardian, phone: d.phone, attendance: d.attendance_pct,
  feeStatus: d.fee_status, feeDue: d.fee_due, status: d.status,
  house: d.house, avatarHue: d.avatar_hue,
});
export const toStudentDTO = (s: Student): StudentDTO => ({
  id: s.id, admission_no: s.adm, name: s.name, gender: s.gender,
  grade: s.grade, section: s.section, class_label: s.cls, roll: s.roll,
  guardian: s.guardian, phone: s.phone, attendance_pct: s.attendance,
  fee_status: s.feeStatus, fee_due: s.feeDue, status: s.status,
  house: s.house, avatar_hue: s.avatarHue,
});
```

- [ ] **Step 5:** `npx vitest run src/lib/mappers.test.ts` → PASS.
- [ ] **Step 6:** `git add src/lib/dtos.ts src/lib/mappers.ts src/lib/mappers.test.ts && git commit -m "feat(admin): canonical Student DTO + mapper (pattern)"`

---

### Task 4: Expand `Api` interface with write methods + add remaining DTOs/mappers + MockApi

**Files:** Modify `src/lib/api.ts`; Modify `src/lib/dtos.ts`, `src/lib/mappers.ts`; Modify `src/lib/api.test.ts`

- [ ] **Step 1: Failing test** — append to `src/lib/api.test.ts`:

```ts
import { MockApi } from '@/lib/api';
describe('MockApi writes', () => {
  it('createStudent returns the student and getStudent finds it', async () => {
    const m = new MockApi();
    const s = await m.createStudent({ id: 'STEST', adm: 'ADM-T', name: 'Test', gender: 'M', grade: 'IX', section: 'A', cls: 'IX-A', roll: 99, guardian: 'G', phone: '1', attendance: 100, feeStatus: 'paid', feeDue: 0, status: 'active', house: 'Red', avatarHue: 10 });
    expect(s.id).toBe('STEST');
    expect((await m.getStudent('STEST'))?.name).toBe('Test');
  });
  it('updateStudent and deleteStudent work', async () => {
    const m = new MockApi();
    const first = (await m.listStudents())[0];
    const upd = await m.updateStudent(first.id, { house: 'Green' });
    expect(upd.house).toBe('Green');
    await m.deleteStudent(first.id);
    expect(await m.getStudent(first.id)).toBeUndefined();
  });
});
```

- [ ] **Step 2:** `npx vitest run src/lib/api.test.ts` → FAIL (methods missing).
- [ ] **Step 3:** In `src/lib/api.ts`, extend the `Api` interface with write methods and implement them in `MockApi` against module-local mutable copies of the `mockDb` arrays. Add to `interface Api`:

```ts
  createStudent(s: Student): Promise<Student>
  updateStudent(id: string, patch: Partial<Student>): Promise<Student>
  deleteStudent(id: string): Promise<void>
  createTeacher(t: Teacher): Promise<Teacher>
  updateTeacher(id: string, patch: Partial<Teacher>): Promise<Teacher>
  deleteTeacher(id: string): Promise<void>
  createStaff(s: Staff): Promise<Staff>
  updateStaff(id: string, patch: Partial<Staff>): Promise<Staff>
  deleteStaff(id: string): Promise<void>
```

In `MockApi`, replace the direct `students`/`teachers`/`staff` imports with mutable copies (`private students = [...students]` etc.) and implement each method (push/splice/Object.assign), returning the affected record. Keep all existing read methods reading from the mutable copies.

- [ ] **Step 4:** Extend `src/lib/dtos.ts` + `src/lib/mappers.ts` with snake_case DTOs + `to*`/`to*DTO` for the remaining entities the `Api` returns — **School, Teacher, Staff, Bus, Exam, Approval, AppNotification, Complaint, Thread, Report, RankInfo** — using `src/types/index.ts` as the field source and §3B for snake_case names (e.g. `School.tz`→`timezone`, `Teacher.desig`→`designation`, `Bus.no`→`bus_no`, `Thread.id` number→string). Add one mapper round-trip test per entity in `mappers.test.ts`.
- [ ] **Step 5:** `npx vitest run src/lib/api.test.ts src/lib/mappers.test.ts` → PASS.
- [ ] **Step 6:** `git add -A src/lib && git commit -m "feat(admin): Api write methods + MockApi writes + remaining DTOs/mappers"`

---

### Task 5: RestApi implementation (canonical routes, Bearer+tenant, mappers)

**Files:** Modify `src/lib/api.ts`; Create `src/lib/api.rest.test.ts`

- [ ] **Step 1: Failing test** — `src/lib/api.rest.test.ts` (mock the http client):

```ts
import { describe, it, expect, vi } from 'vitest';
import { RestApi } from '@/lib/api';

function fakeHttp(json: unknown) {
  return { get: vi.fn().mockResolvedValue(json), post: vi.fn().mockResolvedValue(json), patch: vi.fn().mockResolvedValue(json), delete: vi.fn().mockResolvedValue(undefined) };
}

describe('RestApi', () => {
  it('listStudents GETs /students with filters and maps snake_case', async () => {
    const http = fakeHttp([{ id: 'S1', admission_no: 'A1', name: 'M', gender: 'F', grade: 'IX', section: 'A', class_label: 'IX-A', roll: 1, guardian: 'G', phone: '9', attendance_pct: 90, fee_status: 'paid', fee_due: 0, status: 'active', house: 'Blue', avatar_hue: 1 }]);
    const api = new RestApi(http as any);
    const rows = await api.listStudents({ grade: 'IX' });
    expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/students'));
    expect(rows[0].adm).toBe('A1');
    expect(rows[0].attendance).toBe(90);
  });
  it('createStudent POSTs /students with a snake_case body', async () => {
    const http = fakeHttp({ id: 'S2', admission_no: 'A2', name: 'N', gender: 'M', grade: 'X', section: 'B', class_label: 'X-B', roll: 2, guardian: 'G', phone: '9', attendance_pct: 80, fee_status: 'due', fee_due: 100, status: 'active', house: 'Red', avatar_hue: 2 });
    const api = new RestApi(http as any);
    await api.createStudent({ id: '', adm: 'A2', name: 'N', gender: 'M', grade: 'X', section: 'B', cls: 'X-B', roll: 2, guardian: 'G', phone: '9', attendance: 80, feeStatus: 'due', feeDue: 100, status: 'active', house: 'Red', avatarHue: 2 });
    expect(http.post).toHaveBeenCalledWith('/students', expect.objectContaining({ admission_no: 'A2', fee_status: 'due' }));
  });
});
```

- [ ] **Step 2:** `npx vitest run src/lib/api.rest.test.ts` → FAIL.
- [ ] **Step 3:** In `src/lib/api.ts`, replace the commented `RestApi` sketch with a real `class RestApi implements Api` that takes an `HttpClient` in its constructor and implements every read+write method using canonical routes (`/schools`, `/students` + query, `/students/{id}`, `POST/PATCH/DELETE /students/{id}`, `/teachers`, `/staff`, `/buses`, `/exam-papers`→`listExams` maps `Exam`, `/approvals?role=`, `/notifications`, `/complaints`, `/threads`, `/students/{id}/report?examId=`, `/students/{id}/rank?examId=`) and the `to*`/`to*DTO` mappers from Task 3-4. Build query strings from the `ListXOpts`.
- [ ] **Step 4:** `npx vitest run src/lib/api.rest.test.ts` → PASS.
- [ ] **Step 5:** `git add src/lib/api.ts src/lib/api.rest.test.ts && git commit -m "feat(admin): RestApi against canonical routes (Bearer+tenant)"`

---

### Task 6: Env-driven api selection + auth/tenant accessor

**Files:** Modify `src/lib/api.ts`; Create `src/lib/authAccess.ts`; Create `.env.example`

- [ ] **Step 1:** Create `src/lib/authAccess.ts` — a tiny module-level holder the http client reads (set by `AppProvider` in Task 7), so `api.ts` has no React dependency:

```ts
import type { AuthSnapshot } from './http';
let snapshot: AuthSnapshot = { token: null, tenantId: null };
export const setAuthSnapshot = (s: AuthSnapshot) => { snapshot = s; };
export const getAuthSnapshot = (): AuthSnapshot => snapshot;
```

- [ ] **Step 2:** At the bottom of `src/lib/api.ts`, build the client + select implementation by env:

```ts
import { createHttpClient } from './http';
import { getAuthSnapshot } from './authAccess';

const http = createHttpClient({
  baseUrl: import.meta.env.VITE_API_BASE ?? '/v1',
  getAuth: getAuthSnapshot,
});
export const api: Api =
  import.meta.env.VITE_DATA_SOURCE === 'live' ? new RestApi(http) : new MockApi();
```

- [ ] **Step 3:** Create `.env.example`:

```
# VITE_DATA_SOURCE=mock | live   (default mock)
VITE_DATA_SOURCE=mock
# Base URL of the .NET backend (used only when VITE_DATA_SOURCE=live)
VITE_API_BASE=http://localhost:5080/v1
```

- [ ] **Step 4:** `npx vitest run` (full) → green (env unset ⇒ MockApi, existing behavior preserved).
- [ ] **Step 5:** `git add src/lib/api.ts src/lib/authAccess.ts .env.example && git commit -m "feat(admin): env-driven mock/live api switch"`

---

### Task 7: Rewire AppProvider to load + persist through `api`

**Files:** Modify `src/context/AppProvider.tsx`; Modify `src/context/AppProvider.test.tsx`

This is the change that actually makes the app backend-capable. Keep the same `app.*` surface the screens already use so no screen changes.

- [ ] **Step 1: Failing test** — extend `AppProvider.test.tsx` to assert initial students come from `api.listStudents()` and `addStudent` calls `api.createStudent` (mock `@/lib/api`). (Write the test mocking the `api` module's `listStudents`/`createStudent`.)
- [ ] **Step 2:** Run it → FAIL.
- [ ] **Step 3:** In `AppProvider.tsx`:
  - Publish auth/tenant to the http layer: on login and whenever `schoolId` changes, call `setAuthSnapshot({ token, tenantId: schoolId })`. Add a `token` field to the auth state (demo login sets a placeholder token, e.g. `'demo'`; a real `/auth/login` fills it later — out of scope here).
  - Replace the seeded `useState(students)` initialisation with an effect that calls `api.listStudents()/listTeachers()/listStaff()` on mount (and when `schoolId` changes), storing results in state; add a `loading` flag.
  - Change `addStudent/addTeacher/addStaff` to `async` wrappers that `await api.createStudent(...)` then update state with the returned record. (Screens already `await`-friendly via navigation after save; make `save()` await.)
- [ ] **Step 4:** Update `studentAdd.tsx`/`teacherAdd.tsx`/`staffAdd.tsx` `save()` to `await app.addStudent(...)` before navigating (these already build the domain object). No other screen changes.
- [ ] **Step 5:** Run `AppProvider.test.tsx` + the add-form tests → PASS (mock `api` in those tests).
- [ ] **Step 6:** `git add -A src/context src/screens/school && git commit -m "feat(admin): AppProvider loads + persists via api; writes go through createX"`

---

### Task 8: Full verification

- [ ] **Step 1:** `npx tsc --noEmit` → clean.
- [ ] **Step 2:** `npm run test` (full Vitest) → all green (existing + new).
- [ ] **Step 3 (manual sanity, mock path):** `npm run dev`, log in, add a student → still works (now via `api.createStudent` on MockApi).
- [ ] **Step 4 (live path, only if a backend is running — otherwise skip):** create `.env.local` with `VITE_DATA_SOURCE=live` + `VITE_API_BASE`, `npm run dev`, confirm requests carry `Authorization` + `X-Tenant-Id`. **Do NOT build the backend as part of this plan.**
- [ ] **Step 5:** `git commit --allow-empty -m "test(admin): backend-ready verified green"` (do not push).

---

## Self-Review

**Spec coverage:** read RestApi (all 14 methods) ✓ T5; write methods (Student/Teacher/Staff create/update/delete) ✓ T4–5; Bearer + `X-Tenant-Id` ✓ T2,T6,T7; canonical snake_case DTOs/routes ✓ T3–5; env mock/live switch ✓ T6; the dead-code problem fixed by rewiring `AppProvider` through `api` ✓ T7; cookie-auth sketch replaced with token+tenant ✓ T2/T5. UI/domain types unchanged.

**Scope guard honored:** no .NET backend work — only `sms-admin` frontend files. Live path is gated "only if a backend is running."

**Placeholder note:** Task 3 implements Student fully as the worked pattern; Task 4 Step 4 applies the identical DTO/mapper pattern to the remaining entities (School, Teacher, Staff, Bus, Exam, Approval, AppNotification, Complaint, Thread, Report, RankInfo) with the §3B snake_case names called out (`tz`→`timezone`, `desig`→`designation`, `no`→`bus_no`, numeric ids→string) — mechanical repetition of the shown pattern, one mapper test each.

**Open follow-ups (NOT in scope):** real `/auth/login` issuing a JWT (demo token placeholder used); update/delete UI (edit/remove screens) — interface methods exist, screens can adopt later; owner-console portfolio reads that span tenants (no `X-Tenant-Id`) vs school-scoped reads.
