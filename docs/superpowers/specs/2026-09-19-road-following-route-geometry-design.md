# Road-Following Route Geometry — Backend Design

Status: Approved (pending final pre-implementation sign-off)
Repo: sms-backend
Depends on: none (this is the canonical contract; sms-admin, sms-teacher-app, sms-student, sms-staff all depend on this spec)

## 1. Objective

Bus route maps across every consumer app currently draw a straight `Polyline`
between raw stop lat/lng coordinates, cutting across buildings. Replace this
with a road-following route geometry computed once per route via the Google
Routes API, persisted in SQL, and served through one canonical additive
endpoint that all frontends consume identically.

## 2. Existing architecture (from audit)

- ASP.NET Core, raw ADO.NET/Dapper (no EF Core), FluentMigrator for schema
  (`db/Sms.Migrations`, `M{4-digit}_{Description}.cs`, currently up to M0206).
- `TransportRoutes(Id, TenantId, Name, CreatedAt)` and
  `RouteStops(Id, TenantId, RouteId, Name, Seq, Lat, Lng)`, indexed on
  `(RouteId, Seq)`. No geometry/polyline column exists anywhere today.
- `TransportController` (`src/Sms.Api/Controllers/TransportController.cs`,
  `[Route("v1/transport")]`, `[Authorize(Policy = Policies.Principal)]`)
  exposes `GET/POST /v1/transport/routes`,
  `GET/POST/PUT/DELETE /v1/transport/routes/{routeId}/stops[...]`, plus fleet
  and trip endpoints. None of these response DTOs will change shape.
- Tenant isolation is enforced via SQL Server Row-Level Security
  (`CREATE SECURITY POLICY rls.{Table}TenantPolicy ...`), stamped per-request
  by `TenantResolutionMiddleware` / `ITenantContext`. Any new table gets the
  same RLS policy.
- No distributed cache (no Redis/IMemoryCache) exists anywhere in this
  codebase — computed data is persisted in SQL instead, consistent with the
  approach below.
- Two existing outbound third-party HTTP integrations establish the pattern
  to copy: Razorpay (`src/Sms.Shared.Kernel/Payments/RazorpayClient.cs`) and,
  more directly analogous (JSON REST API, `Options` + `IsConfigured` guard),
  AiSearch (`src/Sms.Shared.Kernel/AiSearch/AiSearchOptions.cs`,
  `docs/superpowers/plans/2026-08-28-ai-search.md`).
- ETA today is computed via Haversine straight-line distance
  (`BusModule.cs`, `TripRepository.Haversine`). **This spec does not touch
  ETA.**

## 3. Exact files/components involved

New:
- `db/Sms.Migrations/M0207_RouteGeometries.cs` — new table + RLS policy.
- `src/Sms.Shared.Kernel/Routing/GoogleRoutesOptions.cs` — config class.
- `src/Sms.Shared.Kernel/Routing/GoogleRoutesClient.cs` — HTTP client wrapper.
- `src/Sms.Modules.Transport/RouteGeometryModule.cs` — DTOs + Dapper
  repository (`RouteGeometryRepository`), following the `BusModule.cs` /
  `TransportModule.cs` pattern (DTOs + repo + `AddRouteGeometryModule()` DI
  extension in the same file).
- `src/Sms.Application/Services/Transport/RouteGeometryService.cs` —
  orchestrates hash check → cache hit/miss → Google Routes call → persist.

New (corrected during planning — see below):
- `src/Sms.Api/Controllers/RouteGeometryController.cs` — the geometry
  endpoint, as its own controller rather than a new action on
  `TransportController`.

Modified:
- `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` — register
  `GoogleRoutesOptions`, named `HttpClient("google-routes")`,
  `AddRouteGeometryModule()`.
- `appsettings.json` / `appsettings.Development.json` — blank
  `"GoogleRoutes": { "ApiKey": "", "BaseUrl": "https://routes.googleapis.com" }`
  section, matching the `AiSearch` blank-in-source-control convention.

Untouched (explicitly): `TripRepository`, `TransportFleetHub`,
`TransportFleetBroadcaster`, `BusTrackingStatusRules`, any ETA code, any
existing route/stop/fleet/trip endpoint or DTO.

## 4. API contract

New endpoint, additive, does not replace or alter any existing route/stop
response:

```
GET /v1/transport/routes/{routeId}/geometry
Authorize: same viewers as existing route/stop read access for that route
  (Principal/SchoolAdmin/SchoolOwner via Policies.Principal for the admin
  surface; reuse ITransportAuthorizationResolver for any future non-admin
  consumer of this same endpoint, e.g. if a teacher/parent client wants it
  directly rather than via the admin CRM — out of scope for this phase,
  but the endpoint's authz check is written against the resolver, not
  hardcoded to Principal-only, so it extends cleanly later).
```

**Corrected during final review**: the fields below are the logical
payload shape. The actual wire format follows this codebase's existing
global conventions (confirmed against the shipped code, not asserted in
advance): snake_case field names (`ServiceCollectionExtensions.cs`'s
`SnakeCaseNamingPolicy` on MVC JSON options) and a `data`-envelope wrapper
(`ApiControllerBase.OkData` → `DataEnvelope<T>`), matching every other
endpoint in this API — this was never meant to be a special case, the
original draft below just used the wrong casing by mistake. Frontend
consumers must expect `{ "data": { "route_id", "status", "format",
"geometry", "distance_meters", "duration_seconds", "stop_sequence_hash",
"generated_at" } }`, not the unwrapped camelCase shown in the illustrative
JSON blocks that follow.

Response (available):
```json
{
  "data": {
    "route_id": "...",
    "status": "available",
    "format": "google-encoded-polyline",
    "geometry": "a~l~Fjk~uOwHJy@P...",
    "distance_meters": 4210,
    "duration_seconds": 780,
    "stop_sequence_hash": "sha256:...",
    "generated_at": "2026-09-19T10:00:00Z"
  }
}
```

Response (unavailable — no fresh call succeeded and no valid cache exists):
```json
{
  "data": {
    "route_id": "...",
    "status": "unavailable",
    "format": null,
    "geometry": null,
    "distance_meters": null,
    "duration_seconds": null,
    "stop_sequence_hash": "sha256:...",
    "generated_at": null
  }
}
```

`status` is the field every frontend branches on. `geometry: null` must
never be paired with `status: "available"`, and a real polyline must never
be paired with `status: "unavailable"`. Frontends must not fall back to
their legacy straight-line construction when `status === "unavailable"` —
see §7.

## 5. Data model / migration

`M0207_RouteGeometries.cs`:

```
RouteGeometries
  RouteId           uniqueidentifier  PK, FK -> TransportRoutes(Id)
  TenantId          uniqueidentifier  not null
  StopSequenceHash  varchar(64)       not null   -- sha256 hex
  Format            varchar(32)       not null   -- "google-encoded-polyline"
  EncodedPolyline   nvarchar(max)     not null
  DistanceMeters    int               not null
  DurationSeconds   int               not null
  Provider          varchar(32)       not null   -- "google-routes"
  GeneratedAt       datetime2         not null
```

One row per route (upsert on regenerate, keyed by `RouteId`). RLS policy
added identically to the `TransportRoutes`/`RouteStops` pattern in
`M0107_Transport_Routes_Admin.cs`. `Down()` drops the RLS policy then the
table, symmetric with existing migrations.

## 6. Authentication / authorization

`TransportController` keeps `[Authorize(Policy = Policies.Principal)]` at
the class level for all existing admin CRUD actions — unchanged. The
geometry endpoint is **not** added to that controller: ASP.NET Core
combines a class-level `[Authorize]` policy with any action-level one
using AND semantics, so a looser action-level attribute cannot override a
stricter class-level policy. Instead, the geometry endpoint lives in its
own `RouteGeometryController` (`[Route("v1/transport/routes")]`,
`[Authorize]` — any authenticated user), following the exact precedent
already in this codebase of `ParentTransportController`
(`[Route("v1/me")]`, `[Authorize(Policy = Policies.StudentOrParent)]`) —
a second controller under a shared `v1/...` prefix for the same reason.
Real access control is the per-route check against
`ITransportAuthorizationResolver`, extended with
`CanViewRouteAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid routeId, CancellationToken)`
mirroring the existing `CanViewBusAsync` signature and used by
`TransportFleetHub.JoinBus` — not a new authorization concept. A caller
who is authenticated but not authorized for that specific route gets
`403 Forbidden`. Tenant scoping is additionally enforced via the RLS
policy on `RouteGeometries`, using the same `ITenantContext`
session-context mechanism as every other transport table. No anonymous
access, no new claim types, no relaxation of any existing policy.

## 7. Error handling

- Google Routes API call fails (timeout, non-2xx, malformed response) or
  `GoogleRoutesOptions.IsConfigured` is false:
  - If a cached row exists for this route (regardless of hash match), return
    it with `status: "available"` — **stale-but-valid geometry is always
    preferred over no geometry**, per the mandatory-correction requirement
    that we never silently show a straight line as the production route.
  - If no cached row exists at all, return `status: "unavailable"` with all
    geometry fields null. HTTP 200, not an error status — this is a normal,
    expected state the frontend renders explicitly, not an exception.
- The endpoint never throws to the caller for routing-provider failures;
  failures are logged server-side (existing logging conventions) and
  degrade to the cached/unavailable response above.
- Malformed/incomplete route (fewer than 2 placed stops): return
  `status: "unavailable"` without calling Google Routes at all — nothing to
  route.

## 8. Caching / hash behavior

- Hash input: for each stop in `RouteStops` for this route, ordered by
  `Seq`: `StopId|Seq|Lat|Lng`, concatenated and SHA-256'd. Any stop add,
  remove, reorder, or coordinate change produces a different hash.
- On `GET .../geometry`: compute the current hash from `RouteStops`. If a
  `RouteGeometries` row exists with a matching `StopSequenceHash`, return it
  directly — **no Google Routes call**. If the hash differs (or no row
  exists), call Google Routes, upsert the row with the new hash, and return
  the fresh result (or fall back per §7 if the call fails).
- This makes invalidation implicit and automatic — no explicit invalidation
  hooks are added to the stop create/update/delete/reorder endpoints. This
  is deliberately the smallest-risk option: those endpoints are untouched.
- GPS updates, SignalR pushes, map open/close, and page refresh never
  reach this code path at all — they don't touch `RouteStops` and don't
  call this endpoint's regeneration logic.

## 9. Google Routes integration detail

- `GoogleRoutesClient` sends stops (ordered, ≤2 as origin/destination, rest
  as `intermediates`) to `POST https://routes.googleapis.com/directions/v2:computeRoutes`
  with `X-Goog-Api-Key` header (never in the URL/query string) and
  `X-Goog-FieldMask: routes.polyline.encodedPolyline,routes.distanceMeters,routes.duration`.
- Google's `computeRoutes` supports up to 25 waypoints (origin +
  destination + up to 23 intermediates) per request. If a route has more
  stops than that, split into sequential batches of ≤25 (last stop of
  batch N = first stop of batch N+1 to avoid a routing gap), concatenate
  the decoded/re-encoded polylines in order, and sum distance/duration.
  Stop order is never altered.
- API key sourced from `GoogleRoutes__ApiKey` env var (or user-secrets in
  dev), following the exact `AiSearch__ApiKey` convention. Never logged,
  never returned in any response, never present in any frontend bundle.

## 10. Testing

- Unit (`tests/Sms.Tests.Unit/Transport/`): hash computation (stable for
  same input, changes on stop add/remove/reorder/coordinate change),
  waypoint-batching logic for >25 stops preserving order and stitching
  boundaries correctly, `IsConfigured` guard behavior.
- Integration (`tests/Sms.Tests.Integration/Transport/`): endpoint returns
  `available` with cached row on hash match (no outbound call — assert via
  a fake/mocked `IGoogleRoutesClient`), returns fresh geometry on hash
  miss and persists it, returns `unavailable` with no cache and a failing
  provider, returns stale cached geometry with a failing provider when a
  cache exists, tenant isolation (route in tenant A never visible querying
  as tenant B), route with <2 stops returns `unavailable` without an
  outbound call.
- Explicit regression check: existing `TransportTripsTests.cs`,
  `BusEtaTests.cs`, fleet/position/live-snapshot tests continue passing
  unmodified — nothing in this feature touches those code paths.

## 11. Rollback / safety considerations

- Migration is purely additive (new table only) — reversible via `Down()`
  with zero impact on existing data.
- Feature is fully inert until `GoogleRoutes__ApiKey` is configured;
  `IsConfigured = false` makes every request short-circuit to
  `status: "unavailable"` (or cached, if any) with zero outbound calls —
  safe to deploy dark.
- No existing endpoint, DTO, hub, or worker is modified in a
  breaking way — this can ship independently of any frontend change.

## 12. Dependencies

- None upstream. This is the foundational spec — sms-admin, sms-teacher-app,
  sms-student, and sms-staff specs all reference this endpoint's contract
  as fixed input and must not invent their own geometry shape or API.

## 13. Non-goals

- Not changing ETA calculation (stays Haversine-based, unmodified).
- Not changing live GPS ingestion, SignalR hubs, or broadcaster logic.
- Not adding a distributed cache layer (Redis etc.) — SQL persistence with
  hash-based reuse is sufficient at this scale.
- Not touching `sms-catreadmin` at all — confirmed via audit to have no
  transport/map UI; the actual CRM/admin map lives in `sms-admin`. No file
  in that repo is read, referenced, or modified by this work.
- Not rotating, modifying, or otherwise touching the pre-existing Google
  Maps key found in `sms-admin/.env.local` — that is a separate, only
  documented, issue.
- Not introducing any new authorization concept beyond extending
  `ITransportAuthorizationResolver` with one additional per-route method
  that mirrors the existing per-bus method.

## Authorization extension decision (resolved)

Decided: extend `ITransportAuthorizationResolver` with
`CanViewRouteAsync(ClaimsPrincipal, Guid routeId)`, and gate the new
geometry action with `[Authorize]` + this per-route check instead of the
class-level Principal policy (§6). This lets sms-teacher-app, sms-student,
and sms-staff call the same endpoint sms-admin uses, with no separate
endpoint and no relaxation of any existing policy.
