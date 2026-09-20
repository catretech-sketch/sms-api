# Student + Parent API (`sms-student`) — Contract Reference

> **Audience:** the frontend engineer/agent wiring `sms-student` to the live backend.
> **Status:** **Forward contract** (Phase-5). Backend currently implements only Phase 0. Source of truth:
> `sms-student/src/models/index.ts` + `src/services/types.ts` + service layer (**snake_case wire DTOs**
> below) + master §3B/§3C. Machine-readable twin: [`student-api.openapi.yaml`](./student-api.openapi.yaml).
>
> Two roles in one app: **student** and **parent** (multi-child). Endpoints are scoped accordingly.

---

## 1. Conventions
| Concern | Rule |
|---|---|
| Base URL | `{API_BASE_URL}/v1` |
| Casing | **snake_case** wire DTOs |
| Dates | ISO-8601; some display strings ("Today", "2h ago") are presentational — backend returns ISO, client formats |
| Auth | `Authorization: Bearer <access_token>` + `X-Tenant-Id: <tenant_id>` |
| Success/Error | `{ "data": … }` / `{ "error": { "code","message","details" } }` (see §6) |

---

## 2. Auth (unified, §3C)
- **`POST /v1/auth/login`** `{ "email", "password", "role": "student"|"parent" }` → **Session**:
  ```json
  { "access_token","refresh_token",
    "user": { "id","name","email","role": "student|parent" },
    "tenant": { "id","name" } }
  ```
- **`POST /v1/auth/refresh`** `{ "refresh_token" }` → Session · **`GET /v1/auth/me`** · **`POST /v1/auth/logout`** → `204`.
- **`GET /v1/school`** → School: `id`·`name`·`short_name?`·`logo_url`.

> The Session DTO **carries `refresh_token`** (the app currently discards it). Wire the refresh-on-401 + rehydrate-on-launch client task when going live (noted in §3C as a client follow-up).

---

## 3. Student-scoped endpoints (role = student)

### Profile & schedule
- **`GET /v1/students/me`** → Student: `id`·`admission_no`·`name`·`initials`·`grade`·`class_label`·`school`·`house`·`email`·`attendance_pct`·`overall_avg`·`rank`·`rank_of`.
- **`GET /v1/students/me/today`** → TodayBlock[]: `t` (time)·`d` (duration min)·`label`·`subject_id?`·`kind` (`class`·`break`·`meeting`·`club`)·`room?`·`teacher?`.
- **`GET /v1/students/me/peers`** → Peer[]: `id`·`name`·`initials`·`subject` (relation).
- **`GET /v1/students/me/achievements`** → Achievement[]: `id`·`title`·`date`·`icon` (`award`·`star`·`check`·`flag`)·`hue`.

### Subjects, homework, grades, exams
- **`GET /v1/subjects`** / **`GET /v1/subjects/{id}`** → Subject: `id`·`name`·`short`·`teacher`·`avg`·`trend`·`color` (`pink`·`coral`·`teal`·`blue`·`amber`·`mint`).
- **`GET /v1/homework`** / **`GET /v1/homework/{id}`** → Homework: `id`·`assignment_id?`·`title`·`subject_id`·`due_date`·`due_time`·`status` (`todo`·`progress`·`submitted`·`graded`)·`priority` (`low`·`med`·`high`)·`grade?`.
- **`PATCH /v1/homework/{id}`** `{ "status" }` → Homework.
- **`POST /v1/homework/{id}/submit`** → Homework (marks submitted).
- **`GET /v1/grades`** → Grade[]: `id`·`subject_id`·`title`·`score`·`max_marks`·`grade`·`date`.
- **`GET /v1/exam-papers`** → Exam[]: `id`·`name`·`subject_id`·`date`·`start_time`·`duration_min`·`status` (`upcoming`·`graded`)·`max_marks`·`score?`·`grade?`.

### Announcements, directory, chat
- **`GET /v1/announcements?audience=student`** → Announcement[]: `id`·`from`·`role`·`date`·`title`·`body`·`type?`·`pinned?`.
- **`GET /v1/teachers`** → Teacher[]: `id`·`name`·`initials`·`subject`·`online`.
- **`GET /v1/threads?audience=student`** → ChatThread[]: `id`·`name`·`role`·`last_message`·`last_at`·`unread`·`child_id?`·`group?`.
- **`GET /v1/threads/{id}/messages`** → ChatMessage[]: `id`·`thread_id`·`sender_id`·`text`·`is_mine`·`sent_at`.
- **`POST /v1/threads/{id}/messages`** `{ "text" }` → ChatMessage.

---

## 4. Parent-scoped endpoints (role = parent)

### Profile & children
- **`GET /v1/parents/me`** → Parent: `name`·`initials`·`relation`·`email`·`phone`.
- **`GET /v1/parents/me/children`** → Child[]: `id`·`name`·`initials`·`grade`·`school`·`avg`·`attn`·`fee` (status label)·`unread`·`hue`.
- **`GET /v1/children/{child_id}/today`** → ChildToday: `classes[]` (`t`·`label`·`done`·`attn` `present`/`late`/null) · `meals` (`breakfast`·`lunch`) · `pickup`.

### Fees + online payment
- **`GET /v1/children/{child_id}/fees`** → Fee[]: `id`·`period`·`due_date`·`amount`·`status` (`due`·`paid`)·`items[]` (`label`·`amount`)·`paid_on?`·`method?`.
- **`POST /v1/fees/{fee_id}/pay`** → Fee (initiates payment via `IPaymentGateway`; may return a gateway redirect/intent — provider TBD, Razorpay-style assumed). On success `status: "paid"`, `paid_on`, `method` populated.

### PTM, transport, attendance, leave
- **`GET /v1/ptm`** → PTMMeeting[]: `id`·`date`·`time`·`teacher`·`subject`·`child`·`mode`·`status` (`confirmed`·`pending`).
- **`PATCH /v1/ptm/{id}`** `{ "status": "confirmed"|"pending" }` → PTMMeeting.
- **`GET /v1/children/{child_id}/transport`** → Transport: `bus_no`·`driver`·`plate`·`eta`·`pickup_stop`·`next_stops[]` (`stop`·`eta`·`done`·`you?`). (Reuses Phase-4 live trips.)
- **`GET /v1/me/children/bus`** → live bus position for the caller's linked child/children: `student_id`·`student_name`·`admission_no`·`bus_id`·`bus_no`·`route_name?`·`status` (`idle`·`on_route`·`at_stop`·`delayed`)·`lat?`·`lng?`·`speed_kmh?`·`next_stop_name?`·`last_ping_at?`. **Strictly self-scoped:** resolves the parent's own student via `Users.StudentId` → `Students.AdmissionNo` and returns only that child's assigned bus's active trip. Never accepts a student/bus id from the client; all reads are RLS tenant-scoped, so identical bus numbers/routes/GPS in other schools can never leak. Empty array when the account is not linked to a student or the child has no bus assigned.
- **`GET /v1/children/{child_id}/attendance`** → `{ "days": [ { "d", "kind": "present"|"absent"|"late"|"off"|"future" } ], "flags": [ { "id","tone": "absent"|"late","date","reason","action" } ] }`.
- **`GET /v1/children/{child_id}/leave`** → LeaveRequest[] · **`POST /v1/leave`** `{ "child_id","from_date","to_date","reason","note" }` → LeaveRequest: `id`·`child_id`·`type?`·`from_date`·`to_date`·`reason`·`note`·`status` (`pending`·`approved`·`rejected`).

---

## 5. Lists & filters
No pagination today; the only query filter is `?audience=student|parent` on `/announcements` and `/threads`. Backend will add cursor paging where lists grow.

## 6. Frontend reconciliation notes
- **Refresh token:** Session carries `refresh_token` but the app discards it — wire `/auth/refresh` (refresh-on-401) and `/auth/me` (rehydrate-on-launch) when going live.
- **Envelope/base URL:** services currently use bare base URL + bare DTO bodies — reconcile to the `/v1` base + `{data}` unwrap (or bare bodies), consistently across apps.
- **Display strings vs ISO:** several fields are presentational ("Today", "2h ago", "May 5, 2026"). The backend returns ISO-8601; keep the client-side formatters.
- **Canonical resources:** `/threads` (chat), `/exam-papers` (exams), `/homework` carries `assignment_id` linking to the teacher's Assignment (§3C).
- **Payment:** `/fees/{id}/pay` may need a gateway callback/webhook on the backend; the client treats a returned `status: "paid"` as success.
