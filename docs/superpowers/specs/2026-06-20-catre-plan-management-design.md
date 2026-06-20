# Catre Admin — Plan create / edit / publish

**Date:** 2026-06-20
**Status:** Approved
**Scope:** Close three gaps on the `/v1/plans` surface that block the Catre Admin plan editor.

## Problem

The Catre Admin frontend cannot fully manage plans against the current backend:

1. **Create/save returns 500.** The plan editor posts a payload (e.g. the "Sliver"
   per-student plan) that omits `tier` and instead carries `feature_tiers` (a field
   the backend does not model). `PlanUpsertRequest.Tier` deserializes to `null`, and
   the `Plans.Tier` column is `nvarchar(20) NOT NULL` with no default, so
   `dbo.Plan_Upsert`'s INSERT raises a `SqlException` → the endpoint (no validation)
   returns **500**. The plan never saves; `feature_tiers` is silently dropped.
2. **Edit returns 405.** The frontend calls `PATCH /v1/plans/{id}`; no PATCH route is
   mapped (only `GET /v1/plans/{id}`), so the method is rejected.
3. **Publish/Unpublish returns 404.** The frontend calls `POST /v1/plans/{id}/publish`;
   no such route exists.

The expected contract is already pinned down in `docs/api/catreadmin-api.md`:
`PATCH /v1/plans/{id}` and `POST /v1/plans/{id}/publish` body `{ "visibility": "published" }`,
both returning `{ "data": Plan }`. Visibility enum: `published` · `draft`.

## Key facts

- `tier` is currently a **label only** — `TierFeatures.For(tier)` returns the full
  feature catalog for every tier, so the value has no feature-gating effect today.
  It is used for display and the clients-by-tier filter.
- Existing data convention: a plan named "Gold" has tier `"gold"` (see integration
  tests) — name and tier mirror each other.
- System.Text.Json ignores unknown properties, so `feature_tiers` already drops
  harmlessly with no code change. Keeping the single-tier model is intentional.

## Design

### 1. Default `tier` when blank (fixes the 500)

- `Contracts/CatreContracts.cs` — `PlanUpsertRequest.Tier`: `string` → `string?`
  (honest about the optional input). `PlanResponse.Tier` stays non-null; the DB
  always holds a value after this change.
- `Data/PlanRepository.cs` — in `UpsertAsync`, default the tier the same way
  `FeaturesCsv` is already defaulted in that method:
  `Tier = string.IsNullOrWhiteSpace(r.Tier) ? Slug(r.Name) : r.Tier!.Trim()`
  where `Slug(name)` = `name.Trim().ToLowerInvariant()` truncated to the 20-char
  column limit. `"Sliver"` → `"sliver"`.
- No migration / proc change. The column stays `NOT NULL`; a non-null value is
  always passed.

### 2. `PATCH /v1/plans/{id}` — edit (fixes the 405)

- New route in the Plans section of `ModuleEndpoints.cs`. Reuses the existing
  `PlanUpsertRequest` body (the editor holds the full plan); the id from the route
  wins: `repo.UpsertAsync(req with { Id = id })`.
- PATCH = update, not create: `if (await repo.GetAsync(id) is null) return NotFound();`
  before upserting.
- Returns `200 { data: Plan }`.

### 3. `POST /v1/plans/{id}/publish` — publish / unpublish (fixes the 404)

- New route. Body `PublishPlanRequest(string Visibility)` — `"published"` to publish,
  `"draft"` to unpublish.
- Minimal guard: reject any visibility outside `{ published, draft }` with a `400`
  (`bad_request`) — it is a state transition, not free-form input.
- New repo method `SetVisibilityAsync(Guid id, string visibility)`: inline
  `UPDATE dbo.Plans SET Visibility=@visibility WHERE Id=@id; SELECT {Cols} ... WHERE Id=@id;`
  returning the updated row, or `null` → `404`.
- Returns `200 { data: Plan }`.

### Cross-cutting

- All three return the standard `DataEnvelope<PlanResponse>` (`{ "data": Plan }`).
  `POST /v1/plans` (upsert) keeps its `201`.
- Auth unchanged: the whole `/v1` group already requires the `platform` policy.

## Out of scope

- Per-permission RBAC (`plans.manage` granularity) — the group-level `platform`
  gate stays as-is, consistent with the other plan routes.
- Persisting `feature_tiers` — single-tier model retained by decision.
- True field-level partial PATCH — the editor sends the full object, so a
  full-body update with route-id is sufficient.
- Any `Tier` enum / CHECK constraint.

## Testing (integration, `CatreOpsTests`)

1. **Create without `tier`** → `201`; response `tier == "sliver"` (posts the Sliver
   per-student payload, including `feature_tiers` to prove it's ignored).
2. **PATCH existing plan** → `200`; a changed field (e.g. `price`) is reflected.
   **PATCH unknown id** → `404`.
3. **Publish** → `200`, `visibility == "published"`. **Unpublish** → `200`,
   `visibility == "draft"`. **Publish unknown id** → `404`.
