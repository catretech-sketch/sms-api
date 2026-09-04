# Transport Live-Tracking Authorization & Broadcast — Design

## Context

`sms-staff` (the driver/conductor app) already ingests GPS pings into
`dbo.TripPings` via `TripController`/`TripRepository.IngestPingsAsync`, and
has its own live-map screen for drivers/conductors. Three other client apps
need to *consume* a bus's live position, each with a different visibility
rule:

- **sms-admin** (CRM/Admin/Principal/VP web console) — already has a real
  Google-Maps fleet view (`BusMap.tsx`) wired to `TransportFleetHub`.
- **sms-teacher-app** (Teacher + Principal mobile) — polls
  `GET` bus position every 3 seconds; no push.
- **sms-student** (Student + Parent mobile) — has a `ParentTransportScreen`
  ("Bus track") with no map yet, no push.

Two SignalR hubs already exist:

- `LiveHub` (`/hubs/live`, `[Authorize]`) — generic per-user/per-tenant
  event bus. Pushes only `{ type }` to trigger client-side query
  invalidation; no position payload.
- `TransportFleetHub` (`/hubs/transport-fleet`,
  `[Authorize(Policy = Policies.Principal)]`) — pushes real `fleet_update`
  snapshots, but only to a flat `transport-fleet:{tenantId}` group. Any
  Principal/CRM caller sees every bus in their tenant — correct for that
  role, but there is no way to scope a push to "just this one bus" for a
  parent or a duty teacher.

**The gap:** nothing today can safely push one specific bus's live
position to a specific parent (their child's bus only) or teacher (their
duty bus only). Building that authorization is the prerequisite for every
downstream consumer app to move off polling — and the whole reason this is
its own spec rather than starting with a client-side map.

**Scope of this spec:** backend only — `sms-backend`. It defines the
authorization rule, the new hub surface, and the broadcast contract that
`sms-admin`, `sms-teacher-app`, and `sms-student` will consume in later,
separate specs. No client-app changes are part of this spec.

## Goals

- Let a parent, in real time, see the live position of *only* the
  bus/trip their own child is validly assigned to.
- Let a duty teacher, in real time, see the live position of *only* the
  bus/trip they are assigned to as duty teacher — not every bus their
  students happen to ride.
- Let Principal/CRM/School Admin continue to see all buses/trips in their
  own school/tenant scope, unchanged from today.
- Let a driver/conductor continue to see/manage only their own active
  trip, unchanged from today.
- Resolve every authorization decision **server-side** from the
  authenticated caller's identity and existing DB relationships — never
  trust a client-supplied `busId`, `tripId`, `schoolId`, or `tenantId` as
  proof of access.
- Reuse the existing `TransportFleetHub`/`TransportFleetBroadcaster`
  infrastructure rather than adding a third hub connection to every
  client app.

## Non-goals

- Building or changing any client-app UI (map widgets, polling
  replacement) — separate specs per app, after this one ships.
- Changing `TripRepository`, `TripController`'s existing REST contracts,
  or `dbo.TripPings` ingestion shape.
- Changing the existing `transport-fleet:{tenantId}` tenant-wide group
  behavior for Principal/CRM — it is correct today and stays as-is.
- Historical trip playback, analytics, or any non-live-position feature.

## Authorization Matrix

Resolved by a new `ITransportAuthorizationResolver` service, server-side,
from the authenticated `ClaimsPrincipal` plus DB relationships — the
single reusable source of truth for "can this caller see this bus."

| Caller | Rule |
| --- | --- |
| Driver / Conductor | Only the bus of their own currently-active trip (reuses `TripRepository.GetParticipantRoleAsync`). |
| Duty Teacher | Only a bus where `dbo.BusAssignments` names them as the assigned duty teacher, tenant-scoped. Teaching a student who rides a bus does **not** grant access on its own. |
| Principal / SchoolAdmin / SchoolOwner | Any bus within their own school/tenant scope (unchanged — already covered by the existing tenant-wide `transport-fleet:{tenantId}` group; `JoinBus` is a no-op convenience for these roles, not their primary feed). |
| Parent | Only a bus where `dbo.StudentBusAssignments` shows one of *their own* children (derived from caller identity, same pattern as `ParentTransportController.GetChildrenBus`) currently assigned. |
| Anyone else | Denied. |

A denied check fails only that specific `JoinBus` call — it never
disconnects the caller's whole hub connection, so e.g. a parent with two
children on two buses, one assignment revoked, keeps their other live
subscription.

`busId`/`tripId`/`tenantId` arriving from the client are treated as
*claims to verify*, never as *facts* — every check re-derives the caller's
actual tenant, children, or duty assignments from the database.

## Hub Surface

Extend `TransportFleetHub` (no new hub type):

- **New methods**: `JoinBus(int busId)`, `LeaveBus(int busId)`. Each
  checks `ITransportAuthorizationResolver.CanViewBus(Context.User, busId)`
  before adding/removing the connection from group `bus:{busId}`.
- **Existing behavior unchanged**: `OnConnectedAsync` still auto-joins
  `transport-fleet:{tenantId}` for `Policies.Principal` callers, exactly
  as today.
- Group naming: `bus:{busId}` is **bus-keyed, not trip-keyed** — a bus is
  persistent across days/trips, so a client joins once and stays joined
  across multiple trips, rather than needing to look up "today's TripId"
  first and rejoin every morning. The broadcaster resolves whichever trip
  is currently live for that bus at broadcast time.
- **Reconnection**: SignalR does not persist group membership across a
  reconnect. Clients re-call `JoinBus` after every reconnect; this is
  intentional, not an oversight — it re-runs the authorization check
  fresh each time, so a revoked assignment is caught immediately rather
  than trusting stale membership.

## Broadcast Events & Payload

All pushed to group `bus:{busId}` via an extended
`ITransportFleetBroadcaster`:

- **`position_update`** — pushed every time a ping is ingested for that
  bus's active trip (no new throttling; the driver app's existing ping
  cadence is the effective rate limit). Full snapshot, not just
  lat/lng, so consumers never need a follow-up REST call to render:
  ```
  {
    busId, tripId,
    lat, lng, speedKmh, heading,
    status: "moving" | "stopped" | "offline",
    lastUpdateAt,
    etaNextStopMin, nextStopId
  }
  ```
  (`status` and `etaNextStopMin` reuse `BusModule.GetPositionAsync`'s
  existing derivation logic — not duplicated per consumer app.)
- **`trip_started`** — pushed when `TripController`'s start endpoint
  fires for that bus. Payload: `{ busId, tripId, driverId, conductorId, direction, startedAt }`.
- **`trip_ended`** — pushed when `TripController`'s end endpoint fires.
  Payload: `{ busId, tripId, endedAt }`.
- **`status_changed` (offline)** — pushed by a new lightweight background
  hosted service that scans active trips' last-ping timestamps on a
  ~15-30s interval and fires this event the moment a bus crosses **60
  seconds** of ping silence. This is the one state transition nobody's
  ping will ever announce on its own, so it needs an active sweep rather
  than being derived passively from the position stream.

## Error Handling

- `JoinBus` denial: the hub method returns a typed failure result
  distinguishable from "bus doesn't exist" only in server logs — the
  client-visible response for both cases is a generic denial, so an
  unauthorized caller can't use response differences to enumerate valid
  bus IDs.
- Background offline-sweep failures are logged and skipped for that
  cycle; never fatal to the hub or to ping ingestion.
- No changes to existing REST error handling.

## Testing

- Unit tests for `ITransportAuthorizationResolver` covering the full
  matrix (driver/teacher/principal/CRM/parent × authorized/unauthorized),
  as pure logic against fake repository data — mirroring the existing
  test style for `TripRepository`/`BusModule`.
- Unit tests for the offline-sweep hosted service's threshold transition
  (no ping for >60s → `status_changed` fires exactly once, not
  repeatedly, until a new ping arrives).
- Integration-level test (or the closest existing harness pattern in this
  codebase) confirming `JoinBus` actually adds/rejects group membership
  per the matrix.
- No changes required to existing `TripController`/ingestion tests — their
  contracts are untouched.

## Open Items Deferred to Later Specs

- `sms-admin`: no changes needed to keep working (tenant-wide feed
  untouched); a future spec could let it opt into per-bus detail views
  using the new `JoinBus` surface instead of the existing snapshot-only
  feed, if desired.
- `sms-teacher-app`: replacing its 3s REST poll with `JoinBus` + push is a
  separate spec.
- `sms-student`: building the actual parent-facing live map (currently no
  map widget exists) consuming `JoinBus` + push is a separate spec.
