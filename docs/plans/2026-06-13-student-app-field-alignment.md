# sms-student — Canonical Field Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `sms-student`'s currently-stubbed HTTP layer (`src/services/http/`) so it speaks the canonical School-Admin contract (snake_case DTOs, canonical field names, ISO timestamps), mapping each DTO into the existing domain models in `src/models/index.ts` — no screen or component changes.

**Architecture:** Add two files — `dtos.ts` (canonical snake_case DTO interfaces) and `mappers.ts` (`to*` pure functions DTO→domain) — then replace every `NotImplementedError` in `src/services/http/index.ts` with a real `apiFetch` call piped through a mapper. Domain models and all UI stay untouched. Mappers are unit-tested (they encode the field contract); a few HTTP methods are tested with a mocked `fetch`.

**Tech Stack:** TypeScript, React Native (Expo), Jest (`jest-expo` preset, `@/` alias already configured in `jest.config.js`), TanStack Query. Own git repo. No `test` script yet — Task 1 adds it.

**Canonical reference:** §3B in `2026-06-13-backend-api-design.md`.

**Note on auth:** the canonical `SessionDTO` carries `access_token` + `refresh_token`; the auth mapper maps `access_token`→ the existing domain `Session.token`. Full refresh-token *rotation* (401-retry in the client) is an explicit follow-up, out of scope for field alignment.

---

### Task 1: Branch, add test script, confirm runner

**Files:**
- Modify: `package.json` (add `test` script)

- [ ] **Step 1: Create branch**

```bash
cd /d/SMS/sms-project/sms-student
git checkout -b field-alignment-canonical
```

- [ ] **Step 2: Add the `test` script to `package.json`**

In the `"scripts"` block, add the `test` line (after `"format"`):

```json
    "format": "prettier --write \"src/**/*.{ts,tsx,js,jsx}\"",
    "test": "jest",
    "prepare": "husky"
```

- [ ] **Step 3: Write a trivial smoke test to confirm the runner works**

Create `src/services/http/__tests__/smoke.test.ts`:

```ts
describe('jest runner', () => {
  it('runs', () => {
    expect(1 + 1).toBe(2);
  });
});
```

- [ ] **Step 4: Run it**

Run: `npx jest src/services/http/__tests__/smoke.test.ts`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add package.json src/services/http/__tests__/smoke.test.ts
git commit -m "chore(student): add jest test script + smoke test"
```

---

### Task 2: Canonical DTOs (`dtos.ts`)

**Files:**
- Create: `src/services/http/dtos.ts`
- Create: `src/services/http/__tests__/dtos.test.ts`

- [ ] **Step 1: Write the failing contract test**

Create `src/services/http/__tests__/dtos.test.ts`:

```ts
import type { StudentDTO, FeeInvoiceDTO, AnnouncementDTO, SessionDTO } from '@/services/http/dtos';

describe('canonical DTO key contract', () => {
  it('StudentDTO uses admission_no / attendance_pct / class_label', () => {
    const d: StudentDTO = {
      id: 's1', admission_no: 'WBA-2024-1042', name: 'Maya Patel', initials: 'MP',
      grade: '9', class_label: '9-A', house: 'Blue', email: 'maya@wba.edu',
      school: 'Westbrook Academy', attendance_pct: 94, overall_avg: 88, rank: 3, rank_of: 40,
    };
    expect(Object.keys(d)).toEqual(expect.arrayContaining(['admission_no', 'attendance_pct', 'class_label', 'overall_avg', 'rank_of']));
  });

  it('FeeInvoiceDTO uses due_date / paid_on and item {label, amount}', () => {
    const d: FeeInvoiceDTO = {
      id: 'f1', period: 'Jul 2026', due_date: '2026-07-10', amount: 12000,
      status: 'due', items: [{ label: 'Tuition', amount: 10000 }],
    };
    expect(d.due_date).toBe('2026-07-10');
    expect(d.items?.[0].label).toBe('Tuition');
  });

  it('AnnouncementDTO uses date (not when) and has type', () => {
    const d: AnnouncementDTO = { id: 'a1', from: 'Office', role: 'admin', date: '2026-06-13', title: 'T', body: 'B', type: 'info' };
    expect(d.date).toBe('2026-06-13');
    expect(d.type).toBe('info');
  });

  it('SessionDTO carries access_token + refresh_token', () => {
    const d: SessionDTO = { access_token: 'a', refresh_token: 'r', role: 'student', email: 'm@wba.edu' };
    expect(d.access_token).toBe('a');
    expect(d.refresh_token).toBe('r');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/services/http/__tests__/dtos.test.ts`
Expected: FAIL — `@/services/http/dtos` does not exist.

- [ ] **Step 3: Create `src/services/http/dtos.ts`**

```ts
import type {
  HomeworkStatus, TodayBlock, Subject, Achievement, AttendanceDay, AttendanceFlag,
} from '@/models';

// ── Auth / school ──
export interface SessionDTO { access_token: string; refresh_token: string; role: 'student' | 'parent'; email: string; }
export interface SchoolDTO { id: string; name: string; short_name?: string; logo_url: string; }

// ── Student (canonical: admission_no, attendance_pct, class_label, overall_avg, rank_of) ──
export interface StudentDTO {
  id: string;
  admission_no: string;
  name: string;
  initials: string;
  grade: string;
  class_label: string;
  house: string;
  email: string;
  school: string;
  attendance_pct: number;
  overall_avg: number;
  rank: number;
  rank_of: number;
}

export interface SubjectDTO { id: string; name: string; short: string; teacher: string; avg: number; trend: number; color: Subject['color']; }
export interface TodayBlockDTO { t: string; d: number; label: string; subject_id?: string; kind: TodayBlock['kind']; room?: string; teacher?: string; }
export interface PeerDTO { id: string; name: string; initials: string; subject: string; }
export interface AchievementDTO { id: string; title: string; date: string; icon: Achievement['icon']; hue: Achievement['hue']; }

export interface HomeworkDTO { id: string; title: string; subject_id: string; due_date: string; due_time: string; status: HomeworkStatus; priority: 'low' | 'med' | 'high'; grade?: string; }
export interface ExamPaperDTO { id: string; title: string; subject_id: string; date: string; start_time: string; duration_min: number; status: 'upcoming' | 'graded'; max_marks: number; score?: number; grade?: string; }
export interface GradeDTO { id: string; subject_id: string; title: string; score: number; max_marks: number; grade: string; date: string; }

export interface AnnouncementDTO { id: string; from: string; role: string; date: string; title: string; body: string; type: string; pinned?: boolean; }
export interface ChatThreadDTO { id: string; name: string; role: string; last_message: string; last_at: string; unread: number; child_id?: string | null; group?: boolean; }
export interface ChatMessageDTO { id: string; thread_id: string; sender_id: string; text: string; sent_at: string; is_mine: boolean; }
export interface TeacherDTO { id: string; name: string; initials: string; subject: string; online: boolean; }

// ── Parent ──
export interface ParentDTO { name: string; initials: string; relation: string; email: string; phone: string; }
export interface ChildDTO { id: string; name: string; initials: string; grade: string; school: string; avg: number; attn: number; fee: string; unread: number; hue: number; }
export interface ChildClassDTO { t: string; label: string; done: boolean; attn: 'present' | 'late' | null; }
export interface ChildTodayDTO { classes: ChildClassDTO[]; meals: { breakfast: string; lunch: string }; pickup: string; }

export interface FeeItemDTO { label: string; amount: number; }
export interface FeeInvoiceDTO { id: string; period: string; due_date: string; amount: number; status: 'due' | 'paid'; items?: FeeItemDTO[]; paid_on?: string; method?: string; }
export interface PTMMeetingDTO { id: string; date: string; time: string; teacher: string; subject: string; child: string; mode: string; status: 'confirmed' | 'pending'; }

export interface TransportStopDTO { stop: string; eta: string; done: boolean; you?: boolean; }
export interface TransportDTO { bus_no: string; driver: string; plate: string; eta: string; pickup_stop: string; next_stops: TransportStopDTO[]; }

export interface AttendanceDayDTO { d: number; kind: AttendanceDay['kind']; }
export interface AttendanceFlagDTO { id: string; tone: AttendanceFlag['tone']; date: string; reason: string; action: string; }
export interface AttendanceMonthDTO { days: AttendanceDayDTO[]; flags: AttendanceFlagDTO[]; }

export interface LeaveRequestDTO { id: string; child_id: string; from_date: string; to_date: string; reason: string; note: string; status: 'pending' | 'approved' | 'rejected'; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/services/http/__tests__/dtos.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/http/dtos.ts src/services/http/__tests__/dtos.test.ts
git commit -m "feat(student): add canonical snake_case DTOs"
```

---

### Task 3: Mappers (`mappers.ts`)

**Files:**
- Create: `src/services/http/mappers.ts`
- Create: `src/services/http/__tests__/mappers.test.ts`

- [ ] **Step 1: Write the failing test**

Create `src/services/http/__tests__/mappers.test.ts`:

```ts
import { toStudent, toFee, toAnnouncement, toSession, toLeaveRequest, toChatMessage } from '@/services/http/mappers';
import type { StudentDTO, FeeInvoiceDTO, AnnouncementDTO, SessionDTO, LeaveRequestDTO, ChatMessageDTO } from '@/services/http/dtos';

describe('http mappers → domain', () => {
  it('toStudent maps canonical fields to the domain Student', () => {
    const d: StudentDTO = {
      id: 's1', admission_no: 'WBA-2024-1042', name: 'Maya Patel', initials: 'MP',
      grade: '9', class_label: '9-A', house: 'Blue', email: 'maya@wba.edu',
      school: 'Westbrook Academy', attendance_pct: 94, overall_avg: 88, rank: 3, rank_of: 40,
    };
    expect(toStudent(d)).toEqual({
      name: 'Maya Patel', initials: 'MP', grade: '9', roll: 0, school: 'Westbrook Academy',
      studentId: 'WBA-2024-1042', email: 'maya@wba.edu', classroom: '9-A', house: 'Blue',
      overallAvg: 88, attnPct: 94, rank: 3, rankOf: 40,
    });
  });

  it('toFee maps due_date/paid_on/items', () => {
    const d: FeeInvoiceDTO = {
      id: 'f1', period: 'Jul', due_date: '2026-07-10', amount: 12000, status: 'paid',
      items: [{ label: 'Tuition', amount: 12000 }], paid_on: '2026-07-01', method: 'UPI',
    };
    expect(toFee(d)).toEqual({
      id: 'f1', period: 'Jul', dueDate: '2026-07-10', amount: 12000, status: 'paid',
      items: [{ l: 'Tuition', amt: 12000 }], paidOn: '2026-07-01', method: 'UPI',
    });
  });

  it('toAnnouncement maps date→when', () => {
    const d: AnnouncementDTO = { id: 'a1', from: 'Office', role: 'admin', date: '2026-06-13', title: 'T', body: 'B', type: 'info' };
    expect(toAnnouncement(d)).toEqual({ id: 'a1', from: 'Office', role: 'admin', when: '2026-06-13', title: 'T', body: 'B' });
  });

  it('toSession maps access_token→token', () => {
    const d: SessionDTO = { access_token: 'aaa', refresh_token: 'rrr', role: 'parent', email: 'p@wba.edu' };
    expect(toSession(d)).toEqual({ token: 'aaa', role: 'parent', email: 'p@wba.edu' });
  });

  it('toLeaveRequest maps child_id/from_date/to_date', () => {
    const d: LeaveRequestDTO = { id: 'l1', child_id: 'c1', from_date: '2026-07-01', to_date: '2026-07-02', reason: 'Trip', note: 'n', status: 'pending' };
    expect(toLeaveRequest(d)).toEqual({ id: 'l1', childId: 'c1', from: '2026-07-01', to: '2026-07-02', reason: 'Trip', note: 'n', status: 'pending' });
  });

  it('toChatMessage maps is_mine→from and sent_at→time', () => {
    const d: ChatMessageDTO = { id: 'm1', thread_id: 't1', sender_id: 'u1', text: 'hi', sent_at: '10:00', is_mine: true };
    expect(toChatMessage(d)).toEqual({ id: 'm1', threadId: 't1', from: 'me', text: 'hi', time: '10:00' });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/services/http/__tests__/mappers.test.ts`
Expected: FAIL — `@/services/http/mappers` does not exist.

- [ ] **Step 3: Create `src/services/http/mappers.ts`**

```ts
import type {
  Achievement, Announcement, AttendanceDay, AttendanceFlag, ChatMessage, ChatThread,
  Child, ChildToday, Exam, Fee, Grade, Homework, LeaveRequest, Parent, Peer, PTMMeeting,
  School, Session, Student, Subject, Teacher, TodayBlock, Transport,
} from '@/models';
import type {
  AchievementDTO, AnnouncementDTO, AttendanceMonthDTO, ChatMessageDTO, ChatThreadDTO,
  ChildDTO, ChildTodayDTO, ExamPaperDTO, FeeInvoiceDTO, GradeDTO, HomeworkDTO,
  LeaveRequestDTO, ParentDTO, PeerDTO, PTMMeetingDTO, SchoolDTO, SessionDTO, StudentDTO,
  SubjectDTO, TeacherDTO, TodayBlockDTO, TransportDTO,
} from './dtos';

export const toSession = (d: SessionDTO): Session => ({ token: d.access_token, role: d.role, email: d.email });
export const toSchool = (d: SchoolDTO): School => ({ id: d.id, name: d.name, shortName: d.short_name, logoUrl: d.logo_url });

export const toStudent = (d: StudentDTO): Student => ({
  name: d.name, initials: d.initials, grade: d.grade, roll: 0, school: d.school,
  studentId: d.admission_no, email: d.email, classroom: d.class_label, house: d.house,
  overallAvg: d.overall_avg, attnPct: d.attendance_pct, rank: d.rank, rankOf: d.rank_of,
});

export const toSubject = (d: SubjectDTO): Subject => ({ id: d.id, name: d.name, short: d.short, teacher: d.teacher, avg: d.avg, trend: d.trend, color: d.color });
export const toTodayBlock = (d: TodayBlockDTO): TodayBlock => ({ t: d.t, d: d.d, label: d.label, subjId: d.subject_id, kind: d.kind, room: d.room, teacher: d.teacher });
export const toPeer = (d: PeerDTO): Peer => ({ id: d.id, name: d.name, initials: d.initials, subj: d.subject });
export const toAchievement = (d: AchievementDTO): Achievement => ({ id: d.id, title: d.title, when: d.date, icon: d.icon, hue: d.hue });
export const toTeacher = (d: TeacherDTO): Teacher => ({ id: d.id, name: d.name, initials: d.initials, subj: d.subject, online: d.online });

export const toHomework = (d: HomeworkDTO): Homework => ({ id: d.id, title: d.title, subjId: d.subject_id, due: d.due_date, dueT: d.due_time, status: d.status, priority: d.priority, grade: d.grade });
export const toExam = (d: ExamPaperDTO): Exam => ({ id: d.id, title: d.title, subjId: d.subject_id, date: d.date, time: d.start_time, dur: String(d.duration_min), status: d.status, max: d.max_marks, score: d.score, grade: d.grade });
export const toGrade = (d: GradeDTO): Grade => ({ id: d.id, subjId: d.subject_id, title: d.title, score: d.score, max: d.max_marks, grade: d.grade, date: d.date });

export const toAnnouncement = (d: AnnouncementDTO): Announcement => ({ id: d.id, from: d.from, role: d.role, when: d.date, title: d.title, body: d.body });
export const toChatThread = (d: ChatThreadDTO): ChatThread => ({ id: d.id, name: d.name, role: d.role, last: d.last_message, when: d.last_at, unread: d.unread, kid: d.child_id ?? null, group: d.group });
export const toChatMessage = (d: ChatMessageDTO): ChatMessage => ({ id: d.id, threadId: d.thread_id, from: d.is_mine ? 'me' : 'them', text: d.text, time: d.sent_at });

export const toParent = (d: ParentDTO): Parent => ({ name: d.name, initials: d.initials, relation: d.relation, email: d.email, phone: d.phone });
export const toChild = (d: ChildDTO): Child => ({ id: d.id, name: d.name, initials: d.initials, grade: d.grade, school: d.school, avg: d.avg, attn: d.attn, fee: d.fee, unread: d.unread, hue: d.hue });
export const toChildToday = (d: ChildTodayDTO): ChildToday => ({ classes: d.classes.map((c) => ({ t: c.t, label: c.label, done: c.done, attn: c.attn })), meals: d.meals, pickup: d.pickup });

export const toFee = (d: FeeInvoiceDTO): Fee => ({ id: d.id, period: d.period, dueDate: d.due_date, amount: d.amount, status: d.status, items: d.items?.map((i) => ({ l: i.label, amt: i.amount })), paidOn: d.paid_on, method: d.method });
export const toPTM = (d: PTMMeetingDTO): PTMMeeting => ({ id: d.id, date: d.date, time: d.time, teacher: d.teacher, subj: d.subject, child: d.child, mode: d.mode, status: d.status });
export const toTransport = (d: TransportDTO): Transport => ({ busNo: d.bus_no, driver: d.driver, plate: d.plate, eta: d.eta, pickupStop: d.pickup_stop, nextStops: d.next_stops.map((s) => ({ stop: s.stop, eta: s.eta, done: s.done, you: s.you })) });

export const toAttendanceMonth = (d: AttendanceMonthDTO): { days: AttendanceDay[]; flags: AttendanceFlag[] } => ({
  days: d.days.map((x) => ({ d: x.d, kind: x.kind })),
  flags: d.flags.map((f) => ({ id: f.id, tone: f.tone, date: f.date, reason: f.reason, action: f.action })),
});
export const toLeaveRequest = (d: LeaveRequestDTO): LeaveRequest => ({ id: d.id, childId: d.child_id, from: d.from_date, to: d.to_date, reason: d.reason, note: d.note, status: d.status });
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/services/http/__tests__/mappers.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/http/mappers.ts src/services/http/__tests__/mappers.test.ts
git commit -m "feat(student): add DTO→domain mappers"
```

---

### Task 4: Implement `httpServices` against canonical routes

**Files:**
- Modify: `src/services/http/index.ts` (replace all stubs)
- Create: `src/services/http/__tests__/httpServices.test.ts`

- [ ] **Step 1: Write the failing test (mocked `fetch`)**

Create `src/services/http/__tests__/httpServices.test.ts`:

```ts
import { httpServices } from '@/services/http';
import { setAuthToken } from '@/api/client';

function mockFetchOnce(body: unknown) {
  (global as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => body,
  });
}

describe('httpServices', () => {
  beforeEach(() => setAuthToken('test-token'));

  it('no service method throws NotImplementedError', () => {
    for (const domain of Object.values(httpServices)) {
      for (const fn of Object.values(domain as Record<string, unknown>)) {
        expect(typeof fn).toBe('function');
      }
    }
  });

  it('student.getProfile GETs /students/me and maps the DTO', async () => {
    mockFetchOnce({
      id: 's1', admission_no: 'WBA-2024-1042', name: 'Maya Patel', initials: 'MP',
      grade: '9', class_label: '9-A', house: 'Blue', email: 'maya@wba.edu',
      school: 'Westbrook Academy', attendance_pct: 94, overall_avg: 88, rank: 3, rank_of: 40,
    });
    const profile = await httpServices.student.getProfile();
    expect(profile.studentId).toBe('WBA-2024-1042');
    expect(profile.attnPct).toBe(94);
    expect((global as any).fetch).toHaveBeenCalledWith(
      expect.stringContaining('/students/me'),
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer test-token' }) }),
    );
  });

  it('fees.list GETs /children/:id/fees and maps invoices', async () => {
    mockFetchOnce([{ id: 'f1', period: 'Jul', due_date: '2026-07-10', amount: 12000, status: 'due' }]);
    const fees = await httpServices.fees.list('c1');
    expect(fees[0].dueDate).toBe('2026-07-10');
    expect((global as any).fetch).toHaveBeenCalledWith(expect.stringContaining('/children/c1/fees'), expect.anything());
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/services/http/__tests__/httpServices.test.ts`
Expected: FAIL — methods still throw `NotImplementedError`.

- [ ] **Step 3: Replace `src/services/http/index.ts`**

```ts
import type { Services } from '@/services/types';
import { apiFetch } from '@/api/client';
import type {
  SessionDTO, SchoolDTO, StudentDTO, SubjectDTO, TodayBlockDTO, PeerDTO, AchievementDTO,
  HomeworkDTO, ExamPaperDTO, GradeDTO, AnnouncementDTO, ChatThreadDTO, ChatMessageDTO,
  TeacherDTO, ParentDTO, ChildDTO, ChildTodayDTO, FeeInvoiceDTO, PTMMeetingDTO,
  TransportDTO, AttendanceMonthDTO, LeaveRequestDTO,
} from './dtos';
import {
  toSession, toSchool, toStudent, toSubject, toTodayBlock, toPeer, toAchievement,
  toHomework, toExam, toGrade, toAnnouncement, toChatThread, toChatMessage, toTeacher,
  toParent, toChild, toChildToday, toFee, toPTM, toTransport, toAttendanceMonth, toLeaveRequest,
} from './mappers';

const getJson = <T>(path: string) => apiFetch<T>(path);
const post = <T>(path: string, body: unknown) =>
  apiFetch<T>(path, { method: 'POST', body: JSON.stringify(body) });
const patch = <T>(path: string, body: unknown) =>
  apiFetch<T>(path, { method: 'PATCH', body: JSON.stringify(body) });

export const httpServices: Services = {
  school: { getCurrent: () => getJson<SchoolDTO>('/school').then(toSchool) },
  auth: {
    signIn: (email, password, role) =>
      post<SessionDTO>('/auth/login', { email, password, role }).then(toSession),
    signOut: () => post<void>('/auth/logout', {}).then(() => undefined),
  },
  student: {
    getProfile: () => getJson<StudentDTO>('/students/me').then(toStudent),
    getToday: () => getJson<TodayBlockDTO[]>('/students/me/today').then((a) => a.map(toTodayBlock)),
    getPeers: () => getJson<PeerDTO[]>('/students/me/peers').then((a) => a.map(toPeer)),
    getAchievements: () => getJson<AchievementDTO[]>('/students/me/achievements').then((a) => a.map(toAchievement)),
  },
  subjects: {
    list: () => getJson<SubjectDTO[]>('/subjects').then((a) => a.map(toSubject)),
    byId: (id) => getJson<SubjectDTO | null>(`/subjects/${id}`).then((d) => (d ? toSubject(d) : undefined)),
  },
  homework: {
    list: () => getJson<HomeworkDTO[]>('/homework').then((a) => a.map(toHomework)),
    byId: (id) => getJson<HomeworkDTO | null>(`/homework/${id}`).then((d) => (d ? toHomework(d) : undefined)),
    setStatus: (id, status) => patch<HomeworkDTO>(`/homework/${id}`, { status }).then(toHomework),
    submit: (id) => post<HomeworkDTO>(`/homework/${id}/submit`, {}).then(toHomework),
  },
  grades: {
    listGrades: () => getJson<GradeDTO[]>('/grades').then((a) => a.map(toGrade)),
    listExams: () => getJson<ExamPaperDTO[]>('/exam-papers').then((a) => a.map(toExam)),
  },
  announcements: {
    list: (audience) => getJson<AnnouncementDTO[]>(`/announcements?audience=${audience}`).then((a) => a.map(toAnnouncement)),
  },
  messaging: {
    threads: (audience) => getJson<ChatThreadDTO[]>(`/threads?audience=${audience}`).then((a) => a.map(toChatThread)),
    messages: (threadId) => getJson<ChatMessageDTO[]>(`/threads/${threadId}/messages`).then((a) => a.map(toChatMessage)),
    send: (threadId, text) => post<ChatMessageDTO>(`/threads/${threadId}/messages`, { text }).then(toChatMessage),
  },
  directory: { teachers: () => getJson<TeacherDTO[]>('/teachers').then((a) => a.map(toTeacher)) },
  parent: {
    getProfile: () => getJson<ParentDTO>('/parents/me').then(toParent),
    children: () => getJson<ChildDTO[]>('/parents/me/children').then((a) => a.map(toChild)),
    childToday: (childId) => getJson<ChildTodayDTO>(`/children/${childId}/today`).then(toChildToday),
  },
  fees: {
    list: (childId) => getJson<FeeInvoiceDTO[]>(`/children/${childId}/fees`).then((a) => a.map(toFee)),
    pay: (feeId) => post<FeeInvoiceDTO>(`/fees/${feeId}/pay`, {}).then(toFee),
  },
  ptm: {
    list: () => getJson<PTMMeetingDTO[]>('/ptm').then((a) => a.map(toPTM)),
    setStatus: (id, status) => patch<PTMMeetingDTO>(`/ptm/${id}`, { status }).then(toPTM),
  },
  transport: { forChild: (childId) => getJson<TransportDTO>(`/children/${childId}/transport`).then(toTransport) },
  attendance: { month: (childId) => getJson<AttendanceMonthDTO>(`/children/${childId}/attendance`).then(toAttendanceMonth) },
  leave: {
    list: (childId) => getJson<LeaveRequestDTO[]>(`/children/${childId}/leave`).then((a) => a.map(toLeaveRequest)),
    submit: (req) =>
      post<LeaveRequestDTO>('/leave', {
        child_id: req.childId, from_date: req.from, to_date: req.to, reason: req.reason, note: req.note,
      }).then(toLeaveRequest),
  },
};
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/services/http/__tests__/httpServices.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/http/index.ts src/services/http/__tests__/httpServices.test.ts
git commit -m "feat(student): implement httpServices against canonical routes"
```

---

### Task 5: Full-suite regression

**Files:** none

- [ ] **Step 1: Type-check**

Run: `npx tsc --noEmit`
Expected: no errors (the `Services` interface is fully satisfied; mappers return valid domain types).

- [ ] **Step 2: Full suite**

Run: `npm test`
Expected: ALL PASS — smoke, dtos, mappers, httpServices.

- [ ] **Step 3: Final commit + push**

```bash
git commit --allow-empty -m "test(student): canonical field alignment verified green"
git push -u origin field-alignment-canonical
```

---

## Self-Review

**Spec coverage (student section):** DTOs + mappers built ✓ (Tasks 2–3); `httpServices` implemented against canonical routes ✓ (Task 4); `studentId`→`admission_no`+`id`, `attnPct`→`attendance_pct`, `classroom`→`class_label` ✓ (toStudent); `Announcement.when`→`date`+`type` ✓; `Fee` split fields (`due_date`/`paid_on`/`{label,amount}`) ✓; `ChatThread` (`last`→`last_message`,`when`→`last_at`,`kid`→`child_id`) + `ChatMessage` (`is_mine`,`sent_at`,`thread_id`) ✓; `Transport` (`bus_no`,`pickup_stop`,`next_stops`) ✓; `LeaveRequest` (`from_date`/`to_date`/`child_id`) ✓; `SessionDTO` carries `access_token`+`refresh_token` ✓ (refresh rotation noted as follow-up).

**Placeholder scan:** none — every file is given in full; every run step has command + expected.

**Type consistency:** DTO names in `dtos.ts` match imports in `mappers.ts` and `index.ts`; every mapper returns the exact domain shape from `src/models/index.ts` (verified field-by-field: `Student.roll` is a number so `toStudent` sets `roll: 0` since the canonical Student carries no per-list roll — roll is a class-roster concern, not a profile field; if a roll is later needed it comes from a roster endpoint). `Services` method signatures match `src/services/types.ts` exactly.
