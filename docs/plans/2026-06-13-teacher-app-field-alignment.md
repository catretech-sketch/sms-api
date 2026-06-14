# sms-teacher-app — Canonical Field Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `sms-teacher-app`'s HTTP DTOs speak the exact canonical School-Admin contract (snake_case field names, canonical enums, canonical routes) without changing any UI component or domain type.

**Architecture:** Only the data-boundary files change — `src/data/http/mappers.ts` (DTO interfaces + `to*`/`from*` mappers) and `src/data/http/*.repo.ts` (routes + request bodies). Each `to*` mapper translates a canonical DTO into the app's *existing* domain type (`src/data/domain/index.ts`), which is left untouched, so screens, hooks, and the mock repos are unaffected. Correctness is locked by new DTO-level mapper tests.

**Tech Stack:** TypeScript, React Native (Expo), Jest (with `@/` path alias), TanStack Query. Each app is its own git repo.

**Canonical reference:** §3B "Canonical data dictionary" in `2026-06-13-backend-api-design.md`.

**Entities that change (verified against current mappers):** Student, Exam→ExamPaper, Grade, AttendanceRecord, LeaveRequest, Announcement, Bus/BusStop. **Already canonical (no change):** Class, TimetableSlot, Assignment, CalendarEvent, LibraryBook, Payslip, Dashboard, Chat, Approval, PrincipalOverview, SchoolAttendance, Session.

---

### Task 1: Branch and capture baseline

**Files:** none (git + verification only)

- [ ] **Step 1: Create a working branch**

```bash
cd /d/SMS/sms-project/sms-teacher-app
git checkout -b field-alignment-canonical
```

- [ ] **Step 2: Run the full test suite to confirm a green baseline**

Run: `npm test`
Expected: all suites PASS (this is the green state every later task must preserve).

- [ ] **Step 3: Commit the branch point (no-op marker)**

```bash
git commit --allow-empty -m "chore: start canonical field alignment"
```

---

### Task 2: Student DTO → canonical

**Files:**
- Create: `src/data/http/__tests__/mappers.canonical.test.ts`
- Modify: `src/data/http/mappers.ts` (replace `StudentDTO` + `toStudent`, lines 77–98)

- [ ] **Step 1: Write the failing test**

Create `src/data/http/__tests__/mappers.canonical.test.ts`:

```ts
import { toStudent, type StudentDTO } from '@/data/http/mappers';

describe('StudentDTO canonical contract', () => {
  const dto: StudentDTO = {
    id: 's1',
    admission_no: 'ADM-001',
    name: 'Maya Patel',
    initials: 'MP',
    gender: 'F',
    class_id: 'c1',
    grade: '6',
    section: 'A',
    class_label: '6-A',
    roll: '12',
    guardian_name: 'Priya Patel',
    guardian_phone: '9876543210',
    attendance_pct: 92,
    fee_status: 'paid',
    fee_due: 0,
    house: 'Blue',
    avatar_hue: 210,
    status: 'active',
  };

  it('declares the canonical snake_case keys', () => {
    expect(Object.keys(dto).sort()).toEqual([
      'admission_no', 'attendance_pct', 'avatar_hue', 'class_id', 'class_label',
      'fee_due', 'fee_status', 'gender', 'grade', 'guardian_name', 'guardian_phone',
      'house', 'id', 'initials', 'name', 'roll', 'section', 'status',
    ]);
  });

  it('maps to the existing domain Student shape', () => {
    expect(toStudent(dto)).toEqual({
      id: 's1', name: 'Maya Patel', roll: '12', initials: 'MP', classId: 'c1',
      attendance: 92, grade: '6', parent: 'Priya Patel', parentPhone: '9876543210',
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts`
Expected: FAIL — `StudentDTO` does not have the canonical fields (`attendance_pct`, `guardian_name`, …); type/shape mismatch.

- [ ] **Step 3: Replace `StudentDTO` and `toStudent` in `src/data/http/mappers.ts`**

Replace the current block (lines 77–98) with:

```ts
export interface StudentDTO {
  id: string;
  admission_no: string;
  name: string;
  initials: string;
  gender: 'M' | 'F';
  class_id: string;
  grade: string;
  section: string;
  class_label: string;
  roll: string;
  guardian_name: string;
  guardian_phone: string;
  attendance_pct: number;
  fee_status: 'paid' | 'partial' | 'due';
  fee_due: number;
  house: string;
  avatar_hue: number;
  status: 'active' | 'inactive';
}
export const toStudent = (d: StudentDTO): Student => ({
  id: d.id,
  name: d.name,
  roll: d.roll,
  initials: d.initials,
  classId: d.class_id,
  attendance: d.attendance_pct,
  grade: d.grade,
  parent: d.guardian_name,
  parentPhone: d.guardian_phone,
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts`
Expected: PASS (both `StudentDTO canonical contract` tests).

- [ ] **Step 5: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): align StudentDTO to canonical school-admin contract"
```

---

### Task 3: Exam → ExamPaper DTO + routes

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `ExamDTO`, `toExam`, `toExamDTO`, lines 227–267)
- Modify: `src/data/http/exams.repo.ts` (rename type import + routes)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

- [ ] **Step 1: Add the failing test (append to `mappers.canonical.test.ts`)**

```ts
import { toExam, toExamDTO, type ExamPaperDTO } from '@/data/http/mappers';

describe('ExamPaperDTO canonical contract', () => {
  const dto: ExamPaperDTO = {
    id: 'p1',
    exam_id: 'e1',
    name: 'Mid-Term Algebra',
    class_id: 'c1',
    class_name: '6-A',
    subject: 'Math',
    date: '2026-06-15',
    start_time: '10:00 AM',
    duration_min: 60,
    max_marks: 50,
    room: 'R-101',
    invigilator1: 'T. Rao',
    invigilator2: 'S. Khan',
    topics: ['Algebra'],
    status: 'upcoming',
  };

  it('maps canonical paper fields to the domain Exam shape', () => {
    expect(toExam(dto)).toEqual({
      id: 'p1', title: 'Mid-Term Algebra', classId: 'c1', className: '6-A',
      subject: 'Math', date: '2026-06-15', time: '10:00 AM', duration: 60,
      maxMarks: 50, topics: ['Algebra'], status: 'upcoming',
    });
  });

  it('writes canonical snake_case keys from a domain patch', () => {
    expect(toExamDTO({ title: 'T', classId: 'c1', duration: 45, maxMarks: 20 })).toEqual({
      name: 'T', class_id: 'c1', duration_min: 45, max_marks: 20,
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "ExamPaperDTO"`
Expected: FAIL — `ExamPaperDTO` is not exported; `toExamDTO` still emits `title`/`duration`/`max_marks` with old keys.

- [ ] **Step 3: Replace the Exam block in `src/data/http/mappers.ts` (lines 227–267)**

```ts
// ─── Exam papers ─────────────────────────────────────────────────────────────
export interface ExamPaperDTO {
  id: string;
  exam_id: string;
  name: string;
  class_id: string;
  class_name: string;
  subject: string;
  date: string;
  start_time: string;
  duration_min: number;
  max_marks: number;
  room: string;
  invigilator1: string;
  invigilator2: string;
  topics: string[];
  status: ExamStatus;
}
export const toExam = (d: ExamPaperDTO): Exam => ({
  id: d.id,
  title: d.name,
  classId: d.class_id,
  className: d.class_name,
  subject: d.subject,
  date: d.date,
  time: d.start_time,
  duration: d.duration_min,
  maxMarks: d.max_marks,
  topics: d.topics,
  status: d.status,
});
export const toExamDTO = (
  e: Partial<Exam> & { classId?: string; maxMarks?: number }
): Partial<ExamPaperDTO> => ({
  ...(e.id !== undefined && { id: e.id }),
  ...(e.title !== undefined && { name: e.title }),
  ...(e.classId !== undefined && { class_id: e.classId }),
  ...(e.className !== undefined && { class_name: e.className }),
  ...(e.subject !== undefined && { subject: e.subject }),
  ...(e.date !== undefined && { date: e.date }),
  ...(e.time !== undefined && { start_time: e.time }),
  ...(e.duration !== undefined && { duration_min: e.duration }),
  ...(e.maxMarks !== undefined && { max_marks: e.maxMarks }),
  ...(e.topics !== undefined && { topics: e.topics }),
  ...(e.status !== undefined && { status: e.status }),
});
```

- [ ] **Step 4: Update `src/data/http/exams.repo.ts` to the renamed type and canonical routes**

Replace the file contents with:

```ts
import type { ExamsRepository, NewExamInput } from '@/data/repositories/types';
import type { HttpClient } from '@/lib/httpClient';
import { toExam, toExamDTO, type ExamPaperDTO } from './mappers';

export function httpExams(http: HttpClient): ExamsRepository {
  return {
    list: () => http.get<ExamPaperDTO[]>('/exam-papers').then((d) => d.map(toExam)),
    get: (id) => http.get<ExamPaperDTO>(`/exam-papers/${id}`).then(toExam),
    create: (input: NewExamInput) =>
      http
        .post<ExamPaperDTO>(
          '/exam-papers',
          toExamDTO({ ...input, classId: input.classId, maxMarks: input.maxMarks })
        )
        .then(toExam),
    update: (id, patch) =>
      http
        .patch<ExamPaperDTO>(
          `/exam-papers/${id}`,
          toExamDTO({ ...patch, classId: patch.classId, maxMarks: patch.maxMarks })
        )
        .then(toExam),
    remove: (id) => http.delete(`/exam-papers/${id}`),
  };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "ExamPaperDTO"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/exams.repo.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): split Exam->ExamPaper DTO and adopt /exam-papers routes"
```

---

### Task 4: Grade DTO → `exam_paper_id`

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `GradeDTO` + `toGrade`, lines 270–285)
- Modify: `src/data/http/grades.repo.ts` (route + request body)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

- [ ] **Step 1: Add the failing test**

```ts
import { toGrade, type GradeDTO } from '@/data/http/mappers';

describe('GradeDTO canonical contract', () => {
  const dto: GradeDTO = {
    id: 'g1',
    student_id: 's1',
    student_name: 'Maya Patel',
    exam_paper_id: 'p1',
    marks: 42,
    max_marks: 50,
    grade: 'A',
    gpa: 3.7,
    pass: true,
    date: '2026-06-16',
  };

  it('maps exam_paper_id into the domain examId field', () => {
    expect(toGrade(dto)).toEqual({
      studentId: 's1', studentName: 'Maya Patel', examId: 'p1',
      marks: 42, maxMarks: 50, grade: 'A',
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "GradeDTO"`
Expected: FAIL — `GradeDTO` has `exam_id`, not `exam_paper_id`/`id`/`gpa`/`pass`/`date`.

- [ ] **Step 3: Replace the Grade block in `src/data/http/mappers.ts` (lines 270–285)**

```ts
// ─── Grades ──────────────────────────────────────────────────────────────────
export interface GradeDTO {
  id: string;
  student_id: string;
  student_name: string;
  exam_paper_id: string;
  marks: number;
  max_marks: number;
  grade: string;
  gpa: number;
  pass: boolean;
  date: string;
}
export const toGrade = (d: GradeDTO): GradeEntry => ({
  studentId: d.student_id,
  studentName: d.student_name,
  examId: d.exam_paper_id,
  marks: d.marks,
  maxMarks: d.max_marks,
  grade: d.grade,
});
```

- [ ] **Step 4: Update `src/data/http/grades.repo.ts` to canonical route + body**

```ts
import type { GradesRepository, GradeInput } from '@/data/repositories/types';
import type { HttpClient } from '@/lib/httpClient';
import { toGrade, type GradeDTO } from './mappers';

export function httpGrades(http: HttpClient): GradesRepository {
  return {
    listByExam: (examId) =>
      http.get<GradeDTO[]>(`/exam-papers/${examId}/grades`).then((d) => d.map(toGrade)),
    upsert: (input: GradeInput) =>
      http
        .put<GradeDTO>('/grades', {
          student_id: input.studentId,
          exam_paper_id: input.examId,
          marks: input.marks,
        })
        .then(toGrade),
  };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "GradeDTO"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/grades.repo.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): align GradeDTO to exam_paper_id canonical contract"
```

---

### Task 5: AttendanceRecord status enum → canonical words

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `AttendanceRecordDTO` + `toAttendanceRecord`, lines 288–297; add `fromAttendanceStatus`)
- Modify: `src/data/http/attendance.repo.ts` (map status code → canonical word on save)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

- [ ] **Step 1: Add the failing test**

```ts
import {
  toAttendanceRecord,
  fromAttendanceStatus,
  type AttendanceRecordDTO,
} from '@/data/http/mappers';

describe('AttendanceRecordDTO canonical contract', () => {
  it('maps canonical status words to the domain P/A/L/V codes', () => {
    const cases: Array<[AttendanceRecordDTO['status'], string]> = [
      ['present', 'P'], ['absent', 'A'], ['late', 'L'], ['leave', 'V'],
    ];
    for (const [word, code] of cases) {
      const dto: AttendanceRecordDTO = { student_id: 's1', status: word, date: '2026-06-13' };
      expect(toAttendanceRecord(dto)).toEqual({ studentId: 's1', status: code, date: '2026-06-13' });
    }
  });

  it('writes canonical status words from domain codes', () => {
    expect(fromAttendanceStatus('P')).toBe('present');
    expect(fromAttendanceStatus('V')).toBe('leave');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "AttendanceRecordDTO"`
Expected: FAIL — `AttendanceRecordDTO.status` is currently `'P'|'A'|'L'|'V'`; `fromAttendanceStatus` is not exported.

- [ ] **Step 3: Replace the Attendance block in `src/data/http/mappers.ts` (lines 288–297)**

```ts
// ─── Attendance (roll-call) ────────────────────────────────────────────────────
export type CanonicalAttendanceStatus = 'present' | 'absent' | 'late' | 'leave' | 'holiday';

const ATT_WORD_TO_CODE: Record<CanonicalAttendanceStatus, AttendanceStatus> = {
  present: 'P', absent: 'A', late: 'L', leave: 'V', holiday: 'A',
};
const ATT_CODE_TO_WORD: Record<AttendanceStatus, CanonicalAttendanceStatus> = {
  P: 'present', A: 'absent', L: 'late', V: 'leave',
};

export interface AttendanceRecordDTO {
  student_id: string;
  status: CanonicalAttendanceStatus;
  date: string;
}
export const toAttendanceRecord = (d: AttendanceRecordDTO): AttendanceRecord => ({
  studentId: d.student_id,
  status: ATT_WORD_TO_CODE[d.status],
  date: d.date,
});
export const fromAttendanceStatus = (s: AttendanceStatus): CanonicalAttendanceStatus =>
  ATT_CODE_TO_WORD[s];
```

- [ ] **Step 4: Update `src/data/http/attendance.repo.ts` to send canonical words on save**

```ts
import type { AttendanceRepository } from '@/data/repositories/types';
import type { HttpClient } from '@/lib/httpClient';
import {
  toAttendanceRecord,
  fromAttendanceStatus,
  type AttendanceRecordDTO,
} from './mappers';

export function httpAttendance(http: HttpClient): AttendanceRepository {
  return {
    forClass: (classId, date) =>
      http
        .get<AttendanceRecordDTO[]>(`/classes/${classId}/attendance?date=${date}`)
        .then((d) => d.map(toAttendanceRecord)),

    save: (classId, date, records) =>
      http
        .post<void>(`/classes/${classId}/attendance`, {
          date,
          records: records.map((r) => ({
            student_id: r.studentId,
            status: fromAttendanceStatus(r.status),
            date: r.date,
          })),
        })
        .then(() => undefined),
  };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "AttendanceRecordDTO"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/attendance.repo.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): map attendance status to canonical present/absent/late/leave"
```

---

### Task 6: LeaveRequest DTO → `from_date`/`to_date`

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `LeaveRequestDTO` + `toLeaveRequest`, lines 337–356; add `fromNewLeave`)
- Modify: `src/data/http/leave.repo.ts` (canonical request body on create)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

- [ ] **Step 1: Add the failing test**

```ts
import { toLeaveRequest, fromNewLeave, type LeaveRequestDTO } from '@/data/http/mappers';

describe('LeaveRequestDTO canonical contract', () => {
  const dto: LeaveRequestDTO = {
    id: 'l1',
    requester_id: 'u1',
    type: 'casual',
    from_date: '2026-07-01',
    to_date: '2026-07-02',
    reason: 'Family',
    substitute: 'T. Rao',
    status: 'pending',
    applied_on: '2026-06-20',
    decided_note: undefined,
  };

  it('maps from_date/to_date/applied_on to the domain from/to/appliedOn', () => {
    expect(toLeaveRequest(dto)).toEqual({
      id: 'l1', type: 'casual', from: '2026-07-01', to: '2026-07-02',
      reason: 'Family', substitute: 'T. Rao', status: 'pending', appliedOn: '2026-06-20',
    });
  });

  it('writes canonical snake_case keys for a new leave request', () => {
    expect(fromNewLeave({ type: 'sick', from: '2026-07-05', to: '2026-07-06', reason: 'Flu' }))
      .toEqual({ type: 'sick', from_date: '2026-07-05', to_date: '2026-07-06', reason: 'Flu' });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "LeaveRequestDTO"`
Expected: FAIL — DTO still uses `from`/`to`/`applied_on` without `from_date`/`to_date`/`requester_id`; `fromNewLeave` not exported.

- [ ] **Step 3: Replace the Leave block in `src/data/http/mappers.ts` (lines 337–356)**

```ts
// ─── Leave ───────────────────────────────────────────────────────────────────
export type CanonicalLeaveType =
  | 'casual' | 'sick' | 'earned' | 'medical' | 'maternity' | 'emergency' | 'other';

export interface LeaveRequestDTO {
  id: string;
  requester_id: string;
  type: CanonicalLeaveType;
  from_date: string;
  to_date: string;
  reason: string;
  substitute?: string;
  status: LeaveStatus;
  applied_on: string;
  decided_note?: string;
}
export const toLeaveRequest = (d: LeaveRequestDTO): LeaveRequest => ({
  id: d.id,
  type: d.type as LeaveType,
  from: d.from_date,
  to: d.to_date,
  reason: d.reason,
  substitute: d.substitute,
  status: d.status,
  appliedOn: d.applied_on,
});
export const fromNewLeave = (
  r: { type: LeaveType; from: string; to: string; reason: string; substitute?: string }
): Partial<LeaveRequestDTO> => ({
  type: r.type as CanonicalLeaveType,
  from_date: r.from,
  to_date: r.to,
  reason: r.reason,
  ...(r.substitute !== undefined && { substitute: r.substitute }),
});
```

- [ ] **Step 4: Update `src/data/http/leave.repo.ts` to send the canonical body**

```ts
import type { LeaveRepository, NewLeaveInput } from '@/data/repositories/types';
import type { HttpClient } from '@/lib/httpClient';
import { toLeaveRequest, fromNewLeave, type LeaveRequestDTO } from './mappers';

export function httpLeave(http: HttpClient): LeaveRepository {
  return {
    list: () => http.get<LeaveRequestDTO[]>('/leave').then((d) => d.map(toLeaveRequest)),

    create: (input: NewLeaveInput) =>
      http.post<LeaveRequestDTO>('/leave', fromNewLeave(input)).then(toLeaveRequest),
  };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "LeaveRequestDTO"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/leave.repo.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): align LeaveRequestDTO to from_date/to_date canonical contract"
```

---

### Task 7: Announcement DTO → add `role`/`audience`

**Files:**
- Modify: `src/data/http/mappers.ts` (replace `AnnouncementDTO` + `toAnnouncement`, lines 149–166)
- Modify: `src/data/http/__tests__/mappers.canonical.test.ts` (add block)

> Note: `AnnouncementsRepository.create` body is the domain `NewAnnouncementInput` (`title`/`body`/`type`), which already uses canonical-compatible keys, so `announcements.repo.ts` needs no change.

- [ ] **Step 1: Add the failing test**

```ts
import { toAnnouncement, type AnnouncementDTO } from '@/data/http/mappers';

describe('AnnouncementDTO canonical contract', () => {
  const dto: AnnouncementDTO = {
    id: 'a1', title: 'Holiday', body: 'School closed', date: '2026-06-13',
    from: 'Principal', role: 'principal', type: 'info', pinned: true, audience: 'all',
  };

  it('exposes role and audience and maps to the domain Announcement', () => {
    expect(dto.role).toBe('principal');
    expect(dto.audience).toBe('all');
    expect(toAnnouncement(dto)).toEqual({
      id: 'a1', title: 'Holiday', body: 'School closed', date: '2026-06-13',
      from: 'Principal', type: 'info', pinned: true,
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "AnnouncementDTO"`
Expected: FAIL — `AnnouncementDTO` has no `role`/`audience`.

- [ ] **Step 3: Replace the Announcement block in `src/data/http/mappers.ts` (lines 149–166)**

```ts
// ─── Announcements ───────────────────────────────────────────────────────────
export interface AnnouncementDTO {
  id: string;
  title: string;
  body: string;
  date: string;
  from: string;
  role?: string;
  type: Announcement['type'];
  pinned?: boolean;
  audience?: string;
}
export const toAnnouncement = (d: AnnouncementDTO): Announcement => ({
  id: d.id,
  title: d.title,
  body: d.body,
  date: d.date,
  from: d.from,
  type: d.type,
  pinned: d.pinned,
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/mappers.canonical.test.ts -t "AnnouncementDTO"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/data/http/mappers.ts src/data/http/__tests__/mappers.canonical.test.ts
git commit -m "feat(teacher): add role/audience to AnnouncementDTO canonical contract"
```

---

### Task 8: Bus/BusStop DTO → `bus_no`/`seq`

**Files:**
- Modify: `src/data/http/bus.repo.ts` (replace `BusStopDTO`, `BusDTO`, `toBus`, lines 5–45)
- Create: `src/data/http/__tests__/bus.canonical.test.ts`

> The `Bus` mappers live inside `bus.repo.ts` (not `mappers.ts`), so this gets its own test file. Domain `BusStop` keeps `order`/`time`; the mapper maps canonical `seq`/`time` into it. Domain `Bus.number` is fed from canonical `bus_no`.

- [ ] **Step 1: Write the failing test**

Create `src/data/http/__tests__/bus.canonical.test.ts`:

```ts
// We test the mapper by importing the internal toBus via a tiny re-export.
// Add `export { toBus, type BusDTO };` at the end of bus.repo.ts in Step 3.
import { toBus, type BusDTO } from '@/data/http/bus.repo';

describe('BusDTO canonical contract', () => {
  const dto: BusDTO = {
    id: 'b1',
    bus_no: 'WBA-07',
    route_name: 'North Loop',
    driver: 'R. Singh',
    driver_phone: '9876500000',
    stops: [{ id: 'st1', name: 'Gate 1', time: '07:45', seq: 1, lat: 40, lng: -75 }],
  };

  it('maps bus_no -> number and stop seq -> order', () => {
    const bus = toBus(dto);
    expect(bus.number).toBe('WBA-07');
    expect(bus.routeName).toBe('North Loop');
    expect(bus.driverPhone).toBe('9876500000');
    expect(bus.stops[0]).toEqual({ id: 'st1', name: 'Gate 1', time: '07:45', order: 1, lat: 40, lng: -75 });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx jest src/data/http/__tests__/bus.canonical.test.ts`
Expected: FAIL — `toBus`/`BusDTO` are not exported; `BusDTO` uses `number`/stop `order`, not `bus_no`/`seq`.

- [ ] **Step 3: Replace the DTOs + `toBus` in `src/data/http/bus.repo.ts` (lines 5–45) and export them**

Replace lines 5–45 with:

```ts
export interface BusStopDTO {
  id: string;
  name: string;
  time: string;
  seq: number;
  lat: number;
  lng: number;
}
export interface BusDTO {
  id: string;
  bus_no: string;
  route_name: string;
  driver: string;
  driver_phone: string;
  stops: BusStopDTO[];
}
interface BusPositionDTO {
  bus_id: string;
  current_stop_index: number;
  progress: number;
  lat: number;
  lng: number;
  next_stop_name: string;
  eta_minutes: number;
}
interface BoardingRecordDTO {
  student_id: string;
  student_name: string;
  initials: string;
  stop_id: string;
  status: BoardingStatus;
}

export const toBus = (d: BusDTO): Bus => ({
  id: d.id,
  number: d.bus_no,
  routeName: d.route_name,
  driver: d.driver,
  driverPhone: d.driver_phone,
  stops: d.stops.map((s) => ({
    id: s.id,
    name: s.name,
    time: s.time,
    order: s.seq,
    lat: s.lat,
    lng: s.lng,
  })),
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx jest src/data/http/__tests__/bus.canonical.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/data/http/bus.repo.ts src/data/http/__tests__/bus.canonical.test.ts
git commit -m "feat(teacher): align Bus DTO to bus_no and stop seq canonical contract"
```

---

### Task 9: Full-suite regression + already-canonical verification

**Files:** none (verification only)

- [ ] **Step 1: Type-check the whole app**

Run: `npx tsc --noEmit`
Expected: no errors. (Catches any screen/hook that referenced a renamed DTO type directly — none should, since domain types are unchanged.)

- [ ] **Step 2: Run the full test suite**

Run: `npm test`
Expected: ALL suites PASS — including the pre-existing domain-level contract tests (`src/__tests__/contracts/contract.ts` consumers) and the new `mappers.canonical.test.ts` / `bus.canonical.test.ts`. Domain types were not changed, so existing tests stay green.

- [ ] **Step 3: Confirm the unchanged DTOs are already canonical**

Visually verify in `src/data/http/mappers.ts` that these DTOs already use snake_case canonical keys and were intentionally left unchanged: `ClassDTO`, `TimetableSlotDTO`, `AssignmentDTO`, `CalendarEventDTO`, `LibraryBookDTO`, `PayslipDTO`, `DashboardStatsDTO`, `ChatContactDTO`, `ChatMessageDTO`, `ApprovalRequestDTO`, `StaffAttendanceEntryDTO`, `PrincipalOverviewDTO`, `SchoolAttendanceDTO`, `SessionDTO`. No edit required.

- [ ] **Step 4: Final commit + push branch**

```bash
git commit --allow-empty -m "test(teacher): canonical field alignment verified green"
git push -u origin field-alignment-canonical
```

---

## Self-Review

**Spec coverage (teacher-app section of the spec):** StudentDTO ✓ (Task 2), Exam→ExamPaper ✓ (Task 3), Grade `exam_paper_id` ✓ (Task 4), Attendance status enum ✓ (Task 5), Leave `from_date`/`to_date` ✓ (Task 6), Announcement `role`/`audience` ✓ (Task 7), Bus `seq`/`bus_no` ✓ (Task 8), routes aligned ✓ (Tasks 3–4), tests updated + green ✓ (Task 9). The spec's "update `src/data/http/__tests__/*`" is satisfied by the new canonical test files; teacher-app had no prior `data/http/__tests__/` folder, so no old DTO tests need editing — the domain-level contract tests in `src/__tests__/contracts/contract.ts` remain valid because domain types are unchanged.

**Placeholder scan:** none — every code step shows full code; every run step shows the command + expected result.

**Type consistency:** `ExamPaperDTO` is used consistently across mappers.ts + exams.repo.ts + grades.repo.ts; `toExam`/`toExamDTO` keep the domain `Exam` signature; `fromAttendanceStatus`/`fromNewLeave` are defined in mappers.ts and imported by their repos; `toBus`/`BusDTO` are exported from bus.repo.ts for the test.
