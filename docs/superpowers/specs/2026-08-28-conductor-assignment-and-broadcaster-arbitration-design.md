# Conductor assignment + single-broadcaster arbitration — design

Status: draft, pending review
Repos touched: `sms-backend` (schema + endpoints), `sms-staff` (Trip screen behavior)
Related: `docs/2026-06-13-backend-api-design.md`, `docs/2026-06-13-frontend-field-alignment-design.md`,
sms-staff memory `sms-staff-live-tracking-program` (SP-1..SP-4 decomposition)

## Context

An end-to-end audit of the bus live-tracking flow (2026-08-28) found that the backend has no data
model for "who is this bus's conductor" at all: `Trips.ConductorId` is a column that's never
written, and there's no `Buses.ConductorStaffId` or equivalent. As a result:

- A conductor can never legally start/ping/end/board a trip today — `RoleChecks.CanOperateTrips`
  doesn't admit conductor claims, and `TripRepository.IsOwnedByDriverAsync` has no conductor
  branch — despite the frontend's `RosterPanel` in `TripScreen.tsx` being gated on
  `role.key === 'conductor'`, which would 403 in production.
- The documented design intent ("driver = primary GPS broadcaster, conductor = fallback
  broadcaster, exactly one active broadcaster per trip") has nothing to bind to: `broadcaster_id`
  is modeled in the frontend's `TripDTO`/mappers but is only ever set by the mock repo, never by
  the real HTTP path.

This spec adds the missing schema and logic to make both of those real.

## Goals

1. A conductor can be assigned to a bus, the same way a driver is today.
2. A conductor can legally operate (start/ping/end/board/view-roster) a trip on their assigned bus.
3. Exactly one of driver/conductor is treated as "the" active broadcaster at any time, computed
   from ping freshness, driver preferred — surfaced to both apps so the conductor's screen can
   decide whether to run its own GPS broadcast.
4. `TripAssignmentDTO.conductor_name` resolves to a real name instead of always `null`.

## Non-goals (explicit scope cuts)

- **Backgrounded auto-wake.** This spec delivers automatic handoff only while the conductor's app
  is foregrounded and polling `trip/current`. If the conductor's phone is locked/backgrounded when
  the driver's GPS goes stale, nothing wakes their app to notice — that needs a background-fetch or
  push-triggered staleness check, which is a separate, larger native-engineering effort. Tracked as
  a future extension, not attempted here.
- **Hard server-side broadcast lockout.** The backend does not reject a driver's or conductor's
  ping based on arbitration — both are always accepted and recorded. Arbitration is
  advisory/display-side: it decides who the *apps* treat as the active broadcaster, not who the
  *server* allows to submit GPS. This avoids a class of bugs where a legitimate ping gets silently
  dropped by a stale arbitration decision (e.g. clock skew, a network race right at a handoff
  boundary) — see "Why accept-always" below.
- **Admin UI changes.** The `sms-admin` screen(s) that call `Bus_Update`/`CreateBusAsync` will need
  a conductor picker to actually assign one, but that UI work is out of scope here — this spec
  only adds the field and the endpoint parameter it needs.

## Schema changes (sms-backend)

New migration, mirroring `M0114_Buses_DriverStaffId`:

- `Buses.ConductorStaffId` — nullable `uniqueidentifier`, same shape as `DriverStaffId`.
- `Trips.DriverLastPingAt`, `Trips.ConductorLastPingAt` — nullable `datetime2`.
- `Bus_Update`/`Bus_Create` procs: add `@ConductorStaffId` parameter, mirroring the existing
  `@DriverStaffId` handling verbatim — including the "clear any other bus's assignment of this
  conductor" uniqueness reassignment (a person can only conduct one bus at a time, same rule
  already applied to drivers).
- `Trip_Start` proc: resolve `Buses.ConductorStaffId → Staff.UserId` for the bus being started
  (same tenant-scoped bus lookup that already resolves `BusId` from `BusNo`), and set
  `Trips.ConductorId` to that resolved user id (`NULL` if the bus has no assigned conductor).
- The application layer (`TripService`, not the stored proc — see "Broadcaster arbitration logic"
  below) updates `DriverLastPingAt`/`ConductorLastPingAt` depending on which role submitted the
  pings.

`Trips.ConductorId` already exists (added when the table was created) — this spec is the first
thing that actually writes to it.

## Authorization changes (sms-backend)

- `RoleChecks.CanOperateTrips`: add a `role == "conductor" || role.Contains("conductor")` branch,
  identical in shape to the existing driver check.
- `TripRepository.IsOwnedByDriverAsync(tripId, userId)` → renamed `IsTripParticipantAsync(tripId,
  userId)`, query becomes `WHERE Id = @tripId AND (DriverId = @userId OR ConductorId = @userId)`.
  Every call site in `TripService` (ping ingest, end, list/upsert boarding, roster) switches to the
  new name — no behavior change for drivers, conductors now pass too.
- `TripOwnershipTests.cs`'s existing "peer driver in the same tenant is 403'd" test still holds
  (a peer driver is neither this trip's `DriverId` nor `ConductorId`). New tests add: the assigned
  conductor CAN operate the trip; a peer conductor (not assigned to this trip) is still 403'd.

## Broadcaster arbitration logic

On every successful `IngestPingsAsync(tripId, req)` call, `TripService` (which already knows the
caller's `uid` and has just confirmed trip participancy) determines the caller's role by comparing
`uid` against the trip's `DriverId`/`ConductorId`, and updates only that role's `LastPingAt`
column via the repository.

`TripResponse` gains:
```
Guid? ConductorId
DateTime? DriverLastPingAt
DateTime? ConductorLastPingAt
string? ActiveBroadcaster   // "driver" | "conductor" | null, computed — see below
```

`ActiveBroadcaster` computation (pure function over the two timestamps, `NOW` = server time,
`STALE = 30s`, matching the existing 10s ping cadence with margin for one missed cycle):

```
if DriverLastPingAt is not null and (NOW - DriverLastPingAt) < STALE:
    "driver"
elif ConductorLastPingAt is not null and (NOW - ConductorLastPingAt) < STALE:
    "conductor"
else:
    null   // neither has pinged recently — trip just started, or both are stale
```

Driver wins ties / is preferred by simply being checked first — matches "driver preferred,
conductor fallback."

### Why accept-always (no server-side rejection)

An earlier version of this design considered having the server reject a conductor's ping outright
if the driver was still active, to enforce a hard single-broadcaster rule. Rejected in favor of
accept-always because:
- A rejected ping is indistinguishable from a network failure to `pingQueue.ts`'s buffer — it would
  requeue and retry rather than discard, meaning stale rejected pings would keep resending forever
  once the driver did go stale, arriving out of order.
- A tiny race at the exact handoff boundary (both apps ping within the same second) would
  non-deterministically 403 one of two legitimate GPS samples, discarding real location data for no
  benefit — the display-side computation already resolves the ambiguity cleanly without needing to
  drop anything.
- The actual goal ("don't drain the conductor's battery running GPS when the driver already is")
  is a client-side decision the conductor's app makes about whether to *start* its own background
  task — the server doesn't need to enforce it by rejecting writes.

## Frontend changes (sms-staff)

- `mappers.ts`: `TripDTO`/`toTrip` already has a `broadcasterId`/`broadcaster_id` field modeled;
  replace it with the new `active_broadcaster` string enum (`driver`/`conductor`/`null`) to match
  the backend contract above — simpler for the client than doing its own clock-skew-sensitive
  timestamp math.
- `TripScreen.tsx`:
  - **Driver**: unchanged — always starts broadcasting on `onStart`, same as today.
  - **Conductor**: on `onStart`, does NOT immediately call `startBroadcast()`. Instead, the
    existing `useCurrentTrip` poll (already running while a trip is live, for the roster/boarding
    UI) drives a `useEffect` that watches `trip.activeBroadcaster`:
    - `"driver"` → conductor's screen shows a small "Driver is sharing location" indicator; if the
      conductor's own broadcast happens to be running (e.g. it was previously active and the driver
      just resumed), call `stopBroadcast()`.
    - `"conductor"` or `null` → if the conductor's own broadcast isn't already running, call
      `startBroadcast()`. (`null` covers "driver hasn't started pinging yet" — conductor picks up
      immediately rather than waiting for a stale-timeout that hasn't started counting.)
  - This satisfies "automatic, no manual tap" for as long as the conductor's screen is open and
    polling — per the Non-goals section, it does not persist across the conductor backgrounding
    their app.

## Testing

- Backend (`Sms.Tests.Integration`, Transport-scoped):
  - Migration applies cleanly (existing `MigrationIdempotenceTests` pattern covers this generically).
  - `Bus_Update`/`Bus_Create` conductor assignment: mirrors existing `BusAssignedTests`-style cases,
    plus the "reassigning a conductor clears their prior bus" uniqueness case.
  - `Trip_Start` sets `ConductorId` from the bus's assigned conductor; `null` when none assigned.
  - Conductor CAN start/ping/end/board/view-roster on their assigned trip; a peer conductor (not
    assigned to this trip) cannot (403).
  - `ActiveBroadcaster` computation: unit-testable as a pure function (extract it rather than
    inlining in the service) — cases: only driver pinged (recent), only conductor pinged, both
    pinged (driver wins), both stale (`null`), neither pinged yet (`null`).
- Frontend (Jest): `TripScreen`'s conductor-branch effect (start/stop broadcast based on
  `activeBroadcaster` transitions) — mock `useCurrentTrip` to emit each transition and assert
  `startBroadcast`/`stopBroadcast` are called at the right times, not called redundantly when the
  state doesn't change.

## Rollout note

This depends on `Buses.ConductorStaffId` actually being set by an admin for a bus to have any
effect — until the (out-of-scope) admin UI picker exists, this ships inert for tenants with no
conductor assigned (`ConductorId` stays `null`, `RosterPanel`'s conductor gating in the frontend
already handles "no conductor" gracefully today).
