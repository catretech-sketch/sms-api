# Teacher + Principal API (`sms-teacher-app`) — Contract Reference

> **Audience:** the frontend engineer/agent wiring `sms-teacher-app` to the live backend.
> **Status:** **Forward contract** (Phase-3). Backend currently implements only Phase 0. Source of truth:
> `sms-teacher-app/src/data/domain/index.ts` + `src/data/http/*.repo.ts` + `mappers.ts` (which already
> define the exact **snake_case wire DTOs** below) + master design §3B/§3C. Machine-readable twin:
> [`teacher-api.openapi.yaml`](./teacher-api.openapi.yaml).

---

## 1. Conventions
| Concern | Rule |
|---|---|
| Base URL | `{API_BASE_URL}/v1` (app env: `EXPO_PUBLIC_API_BASE_URL`) |
| Casing | **snake_case** wire DTOs (mappers convert to camelCase domain) |
| Dates | `YYYY-MM-DD` dates · `HH:MM` times · ISO-8601 timestamps |
| Auth | `Authorization: Bearer <access_token>` + `X-Tenant-Id: <tenant_id>` (both from the session) |
| Roles | `teacher` · `principal` (principal is a superset; principal-only endpoints noted) |
| Success/Error | `{ "data": … }` / `{ "error": { "code","message","details" } }` (see §6 reconciliation) |

---

## 2. Auth (unified, §3C)
- **`POST /v1/auth/login`** `{ "email", "password" }` → **Session**:
  ```json
  { "access_token": "…", "refresh_token": "…",
    "user": { "id","name","initials","title","email","phone","employee","classroom","joined","role": "teacher|principal" },
    "tenant": { "id","name" } }
  ```
- **`POST /v1/auth/refresh`** `{ "refresh_token" }` → Session.
- **`GET /v1/auth/me`** → the `user` object.
- **`POST /v1/auth/logout`** → `204`.

---

## 3. Shared (teacher + principal) endpoints

### Classes & students
- **`GET /v1/classes`** → Class[]: `id`·`name`·`section`·`subject`·`student_count`·`room`·`next_period?`.
- **`GET /v1/classes/{id}`** → Class.
- **`GET /v1/classes/{class_id}/students`** → Student[]: `id`·`admission_no`·`name`·`initials`·`gender`·`class_id`·`grade`·`section`·`class_label`·`roll`·`guardian_name`·`guardian_phone`·`attendance_pct`·`fee_status`·`fee_due`·`house`·`avatar_hue`·`status`.
- **`GET /v1/students/{id}`** → Student.

### Roll-call attendance (teacher-scoped) — §3C
- **`GET /v1/classes/{class_id}/attendance?date=YYYY-MM-DD`** → AttendanceRecord[]: `student_id`·`status` (`present`·`absent`·`late`·`leave`·`holiday`)·`date`.
- **`POST /v1/classes/{class_id}/attendance`** — **bulk upsert**: `{ "date": "YYYY-MM-DD", "records": [ { "student_id","status","date" } ] }` → `204`.
  (Domain codes `P`/`A`/`L`/`V` map to `present`/`absent`/`late`/`leave` on the wire — see §6.)

### Timetable
- **`GET /v1/timetable`** → TimetableSlot[]: `id`·`day` (`Mon`..`Fri`)·`period`·`subject`·`class_id`·`class_name`·`room`·`start_time`·`end_time`.

### Exam papers (teacher CRUD) — canonical `/exam-papers` (§3C)
- **`GET /v1/exam-papers`** · **`GET /v1/exam-papers/{id}`** → ExamPaper: `id`·`exam_id`·`name`·`class_id`·`class_name`·`subject`·`date`·`start_time`·`duration_min`·`max_marks`·`room`·`invigilator1`·`invigilator2`·`topics[]`·`status` (`upcoming`·`completed`·`draft`).
- **`POST /v1/exam-papers`** `{ name, class_id, date, start_time, duration_min, max_marks, topics[], status }` → `201`.
- **`PATCH /v1/exam-papers/{id}`** (partial) · **`DELETE /v1/exam-papers/{id}`** → `204`.

### Grades (teacher upsert)
- **`GET /v1/exam-papers/{exam_id}/grades`** → Grade[]: `id`·`student_id`·`student_name`·`exam_paper_id`·`marks`·`max_marks`·`grade`·`gpa`·`pass`·`date`.
- **`PUT /v1/grades`** — upsert one: `{ "student_id","exam_paper_id","marks" }` → Grade (server computes `grade`/`gpa`/`pass`).

### Assignments (teacher create)
- **`GET /v1/assignments`** → Assignment[]: `id`·`title`·`class_id`·`class_name`·`subject`·`due_date`·`submissions_count`·`total_students`·`status` (`active`·`due_soon`·`overdue`·`closed`)·`description?`·`image_uri?`.
- **`POST /v1/assignments`** `{ title, class_id, due_date, description?, image_uri? }` → `201`.

### Chat — canonical `/threads` (§3C)
- **`GET /v1/threads`** → ChatContact[]: `id`·`name`·`role`·`initials`·`last_message`·`last_at`·`unread`·`online`.
- **`GET /v1/threads/{id}/messages`** → ChatMessage[]: `id`·`thread_id`·`sender_id`·`text`·`sent_at`·`is_mine`.
- **`POST /v1/threads/{id}/messages`** `{ "text" }` → ChatMessage.

### Announcements / calendar / library / payslips
- **`GET /v1/announcements`** → Announcement[]: `id`·`title`·`body`·`date`·`from`·`role?`·`type` (`info`·`warning`·`event`·`urgent`)·`pinned?`·`audience?`.
- **`GET /v1/calendar`** → CalendarEvent[]: `id`·`title`·`date`·`time?`·`type` (`exam`·`holiday`·`meeting`·`event`·`deadline`)·`description?`.
- **`GET /v1/library`** → LibraryBook[]: `id`·`title`·`author`·`subject`·`issued_to?`·`due_date?`·`status` (`available`·`issued`·`overdue`).
- **`GET /v1/payslips`** → PayslipEntry[]: `id`·`month`·`year`·`gross`·`deductions`·`net`·`status` (`paid`·`pending`).

### Dashboard (teacher)
- **`GET /v1/dashboard/stats`** → `total_students`·`total_classes`·`attendance_today`·`pending_assignments`·`upcoming_exams`.

### Geofenced self check-in (teacher)
- **`GET /v1/me/attendance/school-location`** → `lat`·`lng`·`radius_meters`·`name`.
- **`GET /v1/me/attendance/today`** → TeacherAttendanceDay: `date`·`check_in?`·`check_out?` (each CheckEvent: `kind` `in`/`out`·`at`·`lat`·`lng`·`accuracy_meters`·`distance_meters`·`verified`).
- **`GET /v1/me/attendance/history?limit=30`** → TeacherAttendanceDay[].
- **`GET /v1/me/attendance/summary?month=YYYY-MM`** → `days_present`·`days_flagged`·`total_hours`.
- **`POST /v1/me/attendance/punch`** `{ kind, at, lat, lng, accuracy_meters, distance_meters, verified }` → TeacherAttendanceDay. **Server re-verifies** `distance_meters <= radius_meters + min(accuracy_meters, ACCURACY_CAP)` (don't trust client `verified`).

### Leave (teacher)
- **`GET /v1/leave`** → LeaveRequest[]: `id`·`requester_id`·`type` (`casual`·`sick`·`earned`·`medical`·`maternity`·`emergency`·`other`)·`from_date`·`to_date`·`reason`·`substitute?`·`status` (`approved`·`pending`·`rejected`)·`applied_on`·`decided_note?`.
- **`POST /v1/leave`** `{ type, from_date, to_date, reason, substitute? }` → `201`.

### Assigned bus (teacher with bus duty)
- **`GET /v1/bus/assigned`** → Bus: `id`·`bus_no`·`route_name`·`driver`·`driver_phone`·`stops[]` (`id`·`name`·`time`·`seq`·`lat`·`lng`).
- **`GET /v1/bus/{bus_id}/position`** → `bus_id`·`current_stop_index`·`progress`·`lat`·`lng`·`next_stop_name`·`eta_minutes`.
- **`GET /v1/bus/{bus_id}/roster`** → BoardingRecord[]: `student_id`·`student_name`·`initials`·`stop_id`·`status` (`pending`·`boarded`·`absent`).
- **`POST /v1/bus/{bus_id}/boarding`** `{ "records": [BoardingRecord] }` → `204`.

---

## 4. Principal-only endpoints
- **`POST /v1/announcements`** `{ title, body, type }` → broadcast (`201`).
- **`GET /v1/approvals`** → ApprovalRequest[]: `id`·`type` (`leave`·`attendance_correction`)·`requester_id`·`requester_name`·`requester_initials`·`title`·`detail`·`from?`·`to?`·`reason?`·`substitute?`·`priority` (`high`·`medium`·`low`)·`status`·`applied_on`·`decided_note?`.
- **`PATCH /v1/approvals/{id}`** `{ "status": "approved"|"rejected", "decided_note"? }`.
- **`GET /v1/principal/overview`** → `{ "kpis": { students_present_pct, staff_present, staff_total, pending_approvals }, "staff": [ { teacher_id, name, initials, subject, phone, checked_in, check_in_at?, role? } ] }`.
- **`GET /v1/principal/attendance`** → `date`·`present_total`·`student_total`·`overall_pct`·`classes[]` (`class_id`·`class_name`·`present`·`total`·`pct`)·`staff[]`.

---

## 5. Lists
Currently full-list; `/me/attendance/history` takes `?limit=`. The backend will add cursor paging (`limit`/`cursor`) to large lists (students, threads) — code defensively for `next_cursor`.

## 6. Frontend reconciliation notes
- **Attendance enum:** domain uses `P`/`A`/`L`/`V`; wire uses `present`/`absent`/`late`/`leave` (`holiday` collapses to `A` on read). Keep the existing `mappers.ts` conversion.
- **Geofence:** the client computes `distance_meters`/`accuracy_meters`/`verified`, but the **server is authoritative** on `verified`.
- **Envelope/base URL:** the repos currently use a bare base URL and (per `client.ts`) bare DTO bodies. The backend wraps in `{data}` and mounts under `/v1`. Reconcile by pointing the base URL at `…/v1` and unwrapping `.data` in the HTTP client (or have the backend return bare bodies for these resource routes — decide once, consistently).
- **`/threads`** is canonical (the app previously called `/chats`).
