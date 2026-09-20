# Staff API (`sms-staff`) — Contract Reference

> **Audience:** the frontend engineer/agent wiring `sms-staff` to the live backend.
> **Status:** **Forward contract** (Phase-4). Backend currently implements only Phase 0. Source of truth:
> `sms-staff/src/data/domain/*` + `src/data/http/mappers.ts` (exact **snake_case wire DTOs** below) +
> master §3B/§3C. Machine-readable twin: [`staff-api.openapi.yaml`](./staff-api.openapi.yaml).
>
> Non-teaching staff in **6 roles**: `driver` · `conductor` · `guard` · `gardener` · `sweeper` · `peon`.
> Resources are role-namespaced under `/staff/*` (§3C); auth is the shared `/auth/*` surface.

---

## 1. Conventions
| Concern | Rule |
|---|---|
| Base URL | `{API_BASE_URL}/v1` (app env: `API_BASE_URL`) |
| Casing | **snake_case** wire DTOs |
| Dates | ISO-8601 timestamps; `YYYY-MM-DD` dates |
| Auth | `Authorization: Bearer <access_token>` + `X-Tenant-Id: <tenant_id>` |
| Success/Error | `{ "data": … }` / `{ "error": { "code","message","details" } }` (see §6) |

---

## 2. Auth — phone + OTP (§3C polymorphic login)
Staff log in with **phone + role_key** (OTP verification).
- **`POST /v1/auth/login`** `{ "phone": "9876543210", "role_key": "driver" }` → **Session**:
  ```json
  { "access_token","refresh_token",
    "user": { "id","name","first_name","role_key","emp_id","joined","rating","duty_post","shift","timing","phone" },
    "tenant": { "id","name","logo_url" } }
  ```
  `role_key` ∈ `driver`·`conductor`·`guard`·`gardener`·`sweeper`·`peon`. (Backend OTP: console/stub in dev, real provider Phase 6 — the login may be split into request-otp + verify-otp; the app currently posts phone+role_key directly.)
- **`POST /v1/auth/refresh`** `{ "refresh_token" }` → Session.
- **`GET /v1/auth/me`** → the `user` (Staff) object.
- **`POST /v1/auth/logout`** → `204`.

`user` (Staff) canonical superset may also include `category`, `department`, `attendance_pct`, `status`, `avatar_hue`.

---

## 3. Dashboard — `GET /v1/staff/dashboard`
Role-polymorphic. Response:
```json
{ "hours_this_week": 40, "hours_target": 40, "streak_days": 5, "leave_left": 8,
  "role_card": { … see below … }, "pending_tasks_peek": [ { "id","title","priority","done" } ], "alert": null }
```
`role_card` is a discriminated union on `kind`:
| kind | fields |
|---|---|
| `driver` | `bus_no`·`route_name`·`license_expires_in_days`·`fitness_ok` |
| `conductor` | `route_name`·`on_board`·`capacity`·`next_stop` |
| `guard` | `gate`·`rounds_done`·`rounds_total`·`visitors_today` |
| `gardener` | `zones[]`·`watering_due` |
| `sweeper` | `blocks[]`·`supplies_low[]` |
| `peon` | `errands`·`bell_duty` |

> Note: the app's domain `RoleCard` currently uses camelCase inside the card (`busNo`, `licenseExpiresInDays`); expose snake_case (`bus_no`, `license_expires_in_days`) canonically and let the mapper convert. `pending_tasks_peek[].priority` ∈ `urgent`·`normal`.

---

## 4. Resources

### Attendance (geofenced check-in/out)
- **`GET /v1/staff/attendance`** → `checked_in`·`check_in_at?`·`last_log[]` (`at`·`kind` `in`/`out`·`in_zone`)·`duty_post`·`geofence_radius_m`.
- **`POST /v1/staff/attendance/check-in`** `{ "at", "in_zone" }` → Attendance. Server validates geofence (`in_zone`).
- **`POST /v1/staff/attendance/check-out`** `{ "at" }` → Attendance.

### Trips & live GPS (driver/conductor)
- **`GET /v1/staff/trip/assignment`** → `{ "route": { id, name, bus_no, stops[] }, "bus_no", "conductor_name?" }`. Stop: `id`·`name`·`lat`·`lng`·`seq`·`eta_min?`.
- **`GET /v1/staff/trip/current`** → Trip or `null`. Trip: `id`·`route_id`·`bus_no`·`driver_id`·`conductor_id?`·`direction` (`pickup`·`drop`)·`status` (`idle`·`live`·`ended`)·`started_at?`·`ended_at?`·`broadcaster_id?`.
- **`POST /v1/staff/trips`** `{ "route_id", "direction" }` → Trip (status `live`).
- **`POST /v1/staff/trips/{trip_id}/pings`** — **GPS ping ingest** `{ "lat","lng","speed_kmh","heading","at" }` → `204`. The client buffers pings (FIFO) and flushes every 5–10 s; the **backend ingests via TVP** (bulk fan-in) + SignalR fan-out. The backend SHOULD accept a batch too: `{ "pings": [ {…} ] }` (recommend supporting both single + batch).
- **`POST /v1/staff/trips/{trip_id}/end`** → TripSummary: `trip_id`·`duration_min`·`distance_km`·`stops_covered`·`boarded_count`.

### Boarding (conductor)
- **`GET /v1/staff/trips/{trip_id}/roster`** → StudentLite[]: `id`·`name`·`stop_id`·`photo_url?`.
- **`GET /v1/staff/trips/{trip_id}/boarding`** → Boarding[]: `trip_id`·`student_id`·`stop_id`·`state` (`boarded`·`dropped`·`absent`)·`at`.
- **`POST /v1/staff/trips/{trip_id}/boarding`** `{ "student_id","stop_id","state","at" }` → `204`.

### Tasks
- **`GET /v1/staff/tasks`** → Task[]: `id`·`title`·`detail?`·`priority` (`urgent`·`normal`)·`done`·`due_label?`.
- **`POST /v1/staff/tasks/{id}/complete`** → updated Task[].

### Leave
- **`GET /v1/staff/leave`** → `{ "balances": [ { "type","total","used" } ], "requests": [LeaveRequest] }`. LeaveRequest: `id`·`type` (`casual`·`sick`·`earned`; backend union may be wider)·`from_date`·`to_date`·`reason`·`status` (`pending`·`approved`·`rejected`).
- **`POST /v1/staff/leave`** `{ "type","from_date","to_date","reason" }` → LeaveRequest.

### Profile (documents)
- **`GET /v1/staff/profile`** → `{ "documents": [ { "id","label","value","ok?" } ] }`.

---

## 5. Lists
No pagination today; backend will add cursor paging where lists grow (rosters, requests). Code for optional `next_cursor`.

## 6. Frontend reconciliation notes
- **Envelope/base URL:** the app's `httpClient` uses a bare base URL and bare DTO bodies (default `…/v1` already in some builds). Reconcile to the `/v1` base + `{data}` unwrap (or backend returns bare bodies for `/staff/*`) — decide once, consistently across apps.
- **GPS pings:** prefer batching to the backend's TVP path; expect the backend to also expose a SignalR channel for live position fan-out (Phase 4).
- **role_card casing:** convert camelCase card fields to snake_case canonically (see §3 note).
- **OTP:** dev uses a console/stub sender; a production OTP request/verify split may be added — keep the login call abstracted behind `repos.auth.login`.
