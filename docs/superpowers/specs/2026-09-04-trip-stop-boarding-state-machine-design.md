# Trip Stop/Boarding State Machine — Design

## Context

`sms-staff`'s driver/conductor app currently models a trip as a flat
`live`/`ended` lifecycle with a generic `dbo.Boardings` table (student
boarding events, no enforced state values) and no concept of "which stop
is the bus at right now." The live-tracking authorization work already
shipped (`2026-09-04-transport-live-tracking-authorization-design.md`)
gives every authorized viewer (driver, duty teacher, principal/CRM,
parent) a real-time position feed per bus — but that feed has nothing to
say about stop progress, student collection, or the pickup→school→return
lifecycle a real school bus operation runs.

This is the first of five sub-projects redesigning the driver-facing trip
experience end-to-end (backend state machine, then four `sms-staff`
UI sub-projects: pre-trip validation, live map redesign, stop/boarding
UI, school-arrival/return-trip UI). This spec is backend-only — it exists
because none of the UI work is meaningful without real, persisted state
to drive it.

**Scope of this spec:** `sms-backend` only. No `sms-staff` UI changes.

## Goals

- Track which stop a trip is currently at (or "en route between stops")
  without inventing a large literal-state enum — derive fine-grained UI
  states from a small set of canonical trip statuses plus a current-stop
  pointer and per-stop timestamps.
- Detect stop arrival server-side from GPS proximity, but require an
  explicit driver confirmation before the trip's state actually changes
  — proximity alone is advisory, never state-changing.
- Give the driver an explicit "stop completed" action, separate from
  arrival, so a stop only advances when the driver actually says so
  (never inferred from boarding statuses alone).
- Model the return/drop leg as a new trip (not a mutation of the pickup
  trip's direction), reusing the exact same arrival/completion/boarding
  mechanics for pickup and drop.
- Record a school/college arrival checkpoint without ending the trip —
  a trip stays open across the school stay so a return leg can follow.
- Surface GPS accuracy in the broadcast payload; leave the
  LIVE/DELAYED/OFFLINE labeling itself as a frontend-derived
  presentation of `LastUpdateAt`'s age, not a new backend state tier.
- Prevent starting a second active trip on a bus that already has one.

## Non-goals

- Any `sms-staff` UI work (map redesign, status bar, stop-progress
  panel, boarding bottom sheet, pre-trip readiness screen) — those are
  separate sub-project specs that build on this one.
- Changing the existing SignalR authorization/broadcast infrastructure
  itself (`TransportFleetHub`, `ITransportAuthorizationResolver`,
  `JoinBus`/`LeaveBus`) — this spec only adds new broadcast event
  *types* riding the already-authorized `bus:{busId}` group.
- A server-computed "delayed" GPS tier — explicitly left to the
  frontend per the approved design.
- Linking a return trip back to its pickup trip via a foreign key —
  same bus + same day is queryable without one; not needed for this
  spec's scope.

## Trip Status Model

Extend `dbo.Trips.Status` with exactly one new value. Full set:

| Status | Meaning |
| --- | --- |
| `live` (existing) | Trip in progress toward its direction's endpoint (school for `pickup`, final drop point for `drop`). |
| `arrived` (new) | Pickup trip only — reached school/college. Trip stays open; a return trip may follow. Drop trips never use this status — reaching the final stop just leads straight to `ended` via the existing end-trip flow. |
| `ended` (existing) | Trip closed — unchanged meaning and unchanged `EndAsync` contract. |

No status exists for "starting" (that's just the `StartAsync` HTTP call
in flight, not a persisted state) or for at-stop/boarding/departed/next-stop
(all derived — see below).

**Return trips are new `Trip` rows**, not the same trip continuing:
`StartAsync` with `Direction: 'drop'` on the same bus after the pickup
trip reaches `arrived`. This matches the existing one-`Direction`-per-trip
shape (`StartTripRequest(Guid? RouteId, string? BusNo, string Direction)`)
and keeps each leg's stop/boarding history in its own trip's rows — no
schema change needed to `StartTripRequest`/`StartAsync`'s existing
direction handling.

## Current-Stop Pointer & Stop Progress

- Add `CurrentStopId uniqueidentifier NULL` to `dbo.Trips`. `NULL` means
  "en route between stops" (including before the first stop and after
  the last). Non-null means the driver has confirmed arrival at that
  stop and not yet confirmed departure.
- New table `dbo.TripStopProgress`:

  | Column | Type | Notes |
  | --- | --- | --- |
  | `Id` | uniqueidentifier | PK |
  | `TenantId` | uniqueidentifier | NOT NULL, RLS-scoped like every other transport table |
  | `TripId` | uniqueidentifier | NOT NULL |
  | `StopId` | uniqueidentifier | NOT NULL |
  | `Seq` | int | Copied from the route's stop sequence at confirm-arrival time, so stop-progress ordering survives a route's stops being edited later |
  | `ArrivedAt` | datetime2 NULL | When the server first detected the bus within the arrival radius of this stop (advisory, set automatically) |
  | `ConfirmedAt` | datetime2 NULL | When the driver called `confirm-arrival` (state-changing) |
  | `DepartedAt` | datetime2 NULL | When the driver called `complete` (state-changing) |

  A stop counts as **completed** iff `DepartedAt IS NOT NULL`. A stop is
  the **current** stop iff `Trips.CurrentStopId` points to it (implies
  `ConfirmedAt IS NOT NULL AND DepartedAt IS NULL`). Every other stop on
  the route is **upcoming**. This is exactly what section 5 of the UI
  spec ("✓ completed / → CURRENT / ○ upcoming") will read from — one row
  per stop the trip has actually reached, ordered by `Seq`.

## Stop Arrival Detection & Confirmation

**Detection (advisory, automatic):** On every ping ingest
(`TripService.IngestPingsAsync`), after the existing ping-processing
steps, compute the Haversine distance from the latest ping to the trip's
next incomplete stop (lowest `Seq` among the route's stops not yet in
`TripStopProgress` with a `DepartedAt`). Reuse the same Haversine
approach already used for attendance geofencing and ETA computation — no
new distance-math primitive. If within a configurable radius (new
config key `TransportStops:ArrivalRadiusMeters`, default **100** —
looser than attendance's precise campus geofence, matching a bus stop's
real-world approach tolerance rather than a building entrance), include
`withinArrivalRadius: true` and `nextStopId` in the `position_update`
broadcast payload (extends `BusLiveSnapshotResponse`). This never
mutates `Trips` or `TripStopProgress` by itself.

**Confirmation (state-changing, driver-initiated):**
`POST /v1/staff/trips/{tripId}/stops/{stopId}/confirm-arrival`
- Validates the caller is a participant of this trip (reuse
  `GetParticipantRoleAsync`).
- Re-validates proximity server-side against the same radius (rejects a
  stray or premature tap with `403`/`409` — the client-visible detection
  flag is advisory, but the state-changing action is authoritative).
- Rejects if `Trips.CurrentStopId` is already set to a different stop
  (must complete the current stop first) or if `stopId` isn't the
  trip's next incomplete stop by `Seq` (stops must be confirmed in
  order — no skipping ahead).
- Inserts (or updates, if a `TripStopProgress` row already exists from
  detection) with `ArrivedAt` (if not already set) and `ConfirmedAt =
  now`.
- Sets `Trips.CurrentStopId = stopId`.
- Broadcasts `stop_arrived` to `bus:{busId}`: `{ busId, tripId, stopId,
  stopName, confirmedAt }`.

`POST /v1/staff/trips/{tripId}/stops/{stopId}/complete`
- Validates the caller is a participant.
- Validates `Trips.CurrentStopId == stopId` (must be the confirmed
  current stop — can't complete a stop that was never confirmed).
- Sets `TripStopProgress.DepartedAt = now`.
- Sets `Trips.CurrentStopId = NULL`.
- Broadcasts `stop_completed` to `bus:{busId}`: `{ busId, tripId,
  stopId, stopName, departedAt, nextStopId, nextStopName }` (`nextStopId`
  is `null` if this was the last stop before school/the final drop).

**Student boarding** reuses the **existing**
`POST /v1/staff/trips/{tripId}/boarding` endpoint and
`UpsertBoardingAsync` unchanged — no new endpoint. Standardize
`BoardingRequest.State` to exactly three literal values:
`"boarded"`, `"absent"` (pickup trips), `"dropped"` (drop trips). A
student with no `Boardings` row for the trip is implicitly "waiting" —
nothing needs pre-seeding per student per stop. The endpoint's existing
signature (`BoardingRequest(Guid StudentId, Guid? StopId, string State,
DateTime At)`) is untouched; only the accepted `State` values are
standardized.

## School Arrival & Trip Closure

`POST /v1/staff/trips/{tripId}/school-arrived` (new)
- Pickup trips only — reject with `409` if `Direction != 'pickup'` or
  `Status != 'live'`.
- Sets `Status = 'arrived'`, records arrival timestamp and the
  triggering GPS location (last known ping).
- Broadcasts `school_arrived` to `bus:{busId}`: `{ busId, tripId,
  arrivedAt, studentsOnboard }` (`studentsOnboard` = count of `boarded`
  rows across the trip, reusing the existing boarded-count query
  pattern already used for fleet snapshots).
- Does **not** end the trip. `EndAsync`/`POST .../end` is unchanged and
  remains the only way to close a trip — now valid to call on a trip in
  either `live` or `arrived` status (a driver could end a pickup trip
  directly from `arrived` if no return leg is being tracked, or a
  return trip ends normally from `live`).

**Starting the return leg** is an ordinary `StartAsync` call with
`Direction: 'drop'` on the same bus — no new endpoint. The one addition
to `StartAsync` itself (state-machine correctness, not UI, so it belongs
here regardless of which app calls it): **reject starting a trip if the
bus already has a trip in `live` or `arrived` status** (new check before
the existing insert, returning `409` — this is the backend half of the
spec's "bus is not already on another active trip" pre-trip check; the
UI-side readiness screen in a later sub-project surfaces this same
condition proactively before the driver even taps Start).

## GPS Accuracy

Add `Accuracy double NULL` to `PingItem` (currently `PingItem(double
Lat, double Lng, double SpeedKmh, double Heading, DateTime At)`) and the
corresponding `dbo.TripPings`/`dbo.TripPingTvp` column — purely
additive and nullable, so existing callers sending pings without
accuracy are unaffected. Thread it through to `BusLiveSnapshotResponse`
so the broadcast payload can show "GPS accuracy: 8 m." The
LIVE/DELAYED/OFFLINE labeling itself is **not** a new backend status —
the frontend already receives `LastUpdateAt`'s exact age and derives its
own presentation thresholds (per the approved design decision).

## Broadcast Payload Changes

Extend `BusLiveSnapshotResponse` (the `position_update` payload) with:
`CurrentStopId` (nullable Guid), `WithinArrivalRadius` (bool),
`NextStopId` (nullable Guid, the stop the arrival-radius check is
against), `Accuracy` (nullable double).

New broadcast events, all riding the existing authorized `bus:{busId}`
group (no changes to `TransportFleetHub`, `ITransportAuthorizationResolver`,
or how a caller joins the group):
- `stop_arrived`
- `stop_completed`
- `school_arrived`

## Error Handling

- `confirm-arrival` proximity/ordering rejections return `409 Conflict`
  with a machine-readable reason (`"too_far"`, `"wrong_stop_order"`,
  `"already_at_stop"`) so the driver app can show a specific message
  rather than a generic failure.
- `complete` on a stop that isn't the current stop returns `409`.
- `school-arrived` on a non-pickup or non-`live` trip returns `409`.
- `StartAsync`'s new duplicate-active-trip check returns `409` with
  reason `"bus_already_active"`.
- No changes to existing error handling for `StartAsync`/`IngestPingsAsync`/
  `EndAsync`'s pre-existing failure paths (forbidden/not-your-trip).

## Testing

Following this codebase's established pattern — no mocking framework;
DB-touching logic tested via `SqlServerFixture` (real SQL Server,
migrations auto-run); pure logic gets plain xUnit unit tests.

- Unit tests for the Haversine arrival-radius check as a pure function
  (distance-in/boolean-out), mirroring `TransportOfflineSweepRules`'s
  pure-logic pattern from the authorization work.
- Integration tests for `confirm-arrival`: happy path; rejected when
  too far; rejected when a different stop is already current; rejected
  when confirming out of `Seq` order.
- Integration tests for `complete`: happy path advances `CurrentStopId`
  to `NULL` and sets `DepartedAt`; rejected when the stop isn't current.
- Integration tests for `school-arrived`: happy path sets `Status =
  'arrived'` without ending the trip; rejected on a `drop` trip;
  rejected when already `ended`.
- Integration test for `StartAsync`'s new duplicate-active-trip guard:
  starting a second trip on a bus already `live`/`arrived` is rejected;
  starting a `drop` trip after the `pickup` trip reaches `arrived`
  succeeds.
- Integration test confirming `boarding` still accepts exactly
  `"boarded"`/`"absent"`/`"dropped"` and rejects other values (a
  standardization test, not a new endpoint test).
- Integration test confirming the three new broadcast events reach a
  subscriber already `JoinBus`'d to that bus's group (reusing the
  SignalR-client test pattern established in the authorization work).
