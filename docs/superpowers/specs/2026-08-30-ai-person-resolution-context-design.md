# AI Person Resolution & Conversational Context — Design Spec

Status: Approved for planning
Date: 2026-08-30
Scope: Architectural (extends the existing AI Global Search subsystem)

## 1. Objective

Extend `POST /v1/ai/search` (already Platinum-only, already tenant/RBAC-scoped, already 14
read-only intents) with two new capabilities:

1. **Multi-entity person resolution** — "Rahul kaun hai?" must resolve Rahul across students,
   teachers, staff, and admin/owner/principal, determine which type he is, and answer accordingly
   — never assume "person name" means "student."
2. **Conversational follow-ups** — a short-lived, client-echoed `conversation_id` lets a caller ask
   "Kya padhate hain?" / "What does he teach?" and have it resolve against whoever was just
   discussed, in whichever language the conversation is currently using.

This is explicitly framed as the **first of two specs**. A second spec (deferred, not part of this
plan) will wire new data domains — marks, fees, timetable, whole-school counts — into the engine
this spec builds, using the existing "add a handler" pattern. This spec's job is the person-resolution
and context mechanism itself, plus a narrowly-scoped `driver` role addition that naturally piggybacks
on the same `AiIntentAccessRules`/`Policies` work.

Non-goals (explicitly out of scope for this iteration):
- Marks, fees, timetable-as-a-standalone-intent, transport beyond the existing `BusLocationSearch`,
  and whole-school student counts — deferred to Spec 2.
- Automatic learning / self-modifying prompts or regex from unsupported-query logs. The existing
  `AiSearchLogRepository` audit flow is kept exactly as-is: log → human review → intentional
  code change → tests → deploy. Nothing here closes that loop automatically.
- `conductor` as a role — no such role exists anywhere in this codebase today, and nothing in this
  spec introduces one.
- Any new write/mutation capability. Every guarantee from the original AI Search spec (§8, read-only
  enforced by construction — handlers are only ever constructed with query repositories) is inherited
  unchanged.

## 2. Current State (grounding — verified against the merged codebase, not assumed)

- `AiSearchService.SearchAsync`: Platinum feature gate → validation → one-shot, stateless Claude
  classification → write-block → intent-support check → `AiSearchAuthorizationService` scope-clamp →
  one `IAiIntentHandler` → audit log. No conversation state exists anywhere today.
- 14 intents, each a thin handler over an **existing** repository/service — no new data access layer
  was invented for AI in the original build, and this spec does not introduce one either.
- Roles: `school.admin`, `school.owner`, `school.principal`, `school.teacher`, `staff`,
  `student.parent` (`Policies.cs`). `driver` is an informal role-claim string checked only by
  `RoleChecks.CanOperateTrips` for trip start/end — it is **not** in `Policies.All`, and has no row
  in `AiIntentAccessRules`, so it is denied by every AI intent today.
- Person data lives in four places with no existing cross-module query joining them: `dbo.Users`
  (admin/owner/principal — no separate profile table), `Teachers`, `Staff`, `Students`. Each has its
  own repository (`TeacherRepository`, `StaffRepository`, `ISisService`) already used by existing
  intents; `Users` has no dedicated repository used by AI Search today.
- The hard-won invariant from today's work, inherited unchanged: `AiAuthorizationResult`'s scope
  lists (`AllowedChildStudentIds`, `AllowedClassNames`) being null or empty is never "no filter" —
  only `Unrestricted == true` means that. Every new component in this spec must honor it.
- `AiSearchLog` (migration M0160) is the existing audit table — kept as-is; this spec adds one new
  table alongside it, not a replacement.

## 3. Architecture

`AiSearchService.SearchAsync` gains two new steps, both additive:

```
Platinum gate → validation
    → [NEW] conversation_id resolve: load prior turn's (ResolvedEntityId, ResolvedEntityType,
         LanguageOverride, PendingCandidates) IF the id is present, unexpired, and belongs to this
         exact (TenantId, UserId) — otherwise treated as no context, silently
    → classification (prompt now carries: the PersonLookup intent; the prior turn's resolved
         entity + type, as CONTEXT ONLY; and languageDirective detection)
    → write-block → intent-support check
    → AiSearchAuthorizationService (UNCHANGED — runs in full, every turn, regardless of context)
    → [NEW] PersonResolver fan-out — only for PersonLookup, using the caller's just-computed scope
    → [NEW] if a stored entity hint exists, verify it is STILL inside the freshly-computed scope
         before trusting it — never skip this check
    → handler dispatch
    → [NEW] conversation_id save: this turn's resolved entity (or cleared, if none/ambiguous/failed),
         sliding TTL renewed
    → audit log (unchanged)
```

The one load-bearing rule underneath all of this: **`conversation_id` is a conversational
convenience, never an authorization artifact.** It may only ever narrow *which* person the
classifier and resolver are talking about; it can never widen, skip, or cache an authorization
decision. Every single turn re-runs `AiSearchAuthorizationService.AuthorizeAsync` in full, exactly
as if the `conversation_id` were absent — the context is consulted only *after* that fresh
authorization result exists, to check the previously-discussed entity is still inside it.

## 4. New Components

### `IPersonResolver` / `PersonResolver` (`Sms.Application.Services.AiSearch`)

```csharp
public sealed record PersonMatch(Guid Id, string Name, string Type, string? Detail);
// Type: "student" | "teacher" | "staff" | "admin" | "owner" | "principal"
// Detail: one safe disambiguating fact — a student's class label, a teacher's department/subject,
// nothing for admin/owner/principal (name + type is already unambiguous enough in practice; if a
// tenant somehow has two identically-named admins, they still disambiguate on the next attribute
// available without exposing anything new — see open item in §10).

public interface IPersonResolver
{
    Task<IReadOnlyList<PersonMatch>> ResolveAsync(
        string name, AiAuthorizationResult auth, IReadOnlyList<string> callerRoles, CancellationToken ct = default);
}
```

Fans out across the four sources **in parallel**, each query scoped by `auth`:
- **Parent** (`!auth.Unrestricted`, has `AllowedChildStudentIds`): search restricted to
  `ISisService.ListMyChildrenAsync()`'s own results — the same call `GreetByIdHandler` already uses,
  never the open `ListStudentsAsync` roster search.
- **Teacher** (`!auth.Unrestricted`, has `AllowedClassNames`): students filtered through
  `StudentClassScope.ClassMatches` against the teacher's real `Classes` rows (the exact fix applied
  to `GreetByIdHandler` earlier — Grade+Section membership, not label-compaction against free text).
  A teacher's fan-out **never** includes `Teachers`/`Staff`/`Users` — a teacher may look up their own
  students by name, not other staff.
- **Admin/Owner/Principal/Staff** (`auth.Unrestricted == true`): all four sources, tenant-scoped via
  the existing RLS/`ITenantContext` guarantee every repository already provides — no manual tenant
  filter is constructed.
- A new **`IUserDirectoryLookup`** (thin, `Sms.Modules.Identity` or wherever `dbo.Users` already has
  a home) is added specifically to search `Users` by name for admin/owner/principal — this is the one
  genuinely new repository-level query in this spec, since no AI intent has ever needed to search
  `Users` directly before.

Zero matches → no-match. Exactly one → resolved. Two or more → the `NeedsClarification` outcome
(§6), and the candidate set (id, type — never sent to the client) is stored server-side against the
`conversation_id` for the immediate follow-up to resolve against.

### `PersonLookupHandler` (new `IAiIntentHandler`, `Intent => "PersonLookup"`)

Consumes `IPersonResolver`'s result:
- One match → renders by type: `"{Name} is a Teacher. {He/She} teaches {Subjects}."` for a teacher
  (subject list from the same source `TeacherSearchHandler` already reads), student details via the
  same shape `StudentDetailsHandler` already returns, staff role/department, admin/owner/principal
  title only (no sensitive HR-style fields invented).
- Zero matches → `NoMatch`, identical shape to every other intent's no-match (no distinguishing
  signal for "exists but unauthorized" vs. "doesn't exist").
- Multiple matches → `NeedsClarification`.

Follow-up intents ("Kya padhate hain?" / "Kaunsi class?") are **not** new intents — the classifier,
seeing a resolved-entity hint of type `teacher` in its prompt context, classifies these directly to
the existing subject/class-shaped answer using the same resolved entity, without a fresh name search.
This keeps the new surface area to one handler, not N follow-up-shaped handlers per entity type.

### `IAiConversationContextStore` (`Sms.Modules.AiSearch.Data`)

New table, migration **M0161** (next available after M0160 — bump if other work lands first, noted
the same way the original spec flagged this risk):

```sql
CREATE TABLE dbo.AiSearchConversation (
    ConversationId    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId          UNIQUEIDENTIFIER NOT NULL,
    UserId            UNIQUEIDENTIFIER NOT NULL,
    ResolvedEntityId  UNIQUEIDENTIFIER NULL,
    ResolvedEntityType NVARCHAR(20) NULL,
    LanguageOverride  NVARCHAR(10) NULL,       -- 'en' | 'hi' | null
    PendingCandidates NVARCHAR(MAX) NULL,       -- JSON [{id, type}], set only during NeedsClarification
    LastIntent        NVARCHAR(60) NULL,
    ExpiresAt         DATETIME2 NOT NULL
);
CREATE INDEX IX_AiSearchConversation_Expiry ON dbo.AiSearchConversation(ExpiresAt);
```

`PendingCandidates` stores only `{id, type}` pairs — never the `{name, detail}` shown to the
client — so the follow-up resolution re-fetches and re-authorizes each candidate fresh rather than
trusting anything cached from the disambiguation turn.

Read: only when the caller's `(TenantId, UserId)` matches the row exactly — a foreign or tampered
`conversation_id` is silently treated as absent, never an error (no signal is given about whether
that id ever existed, consistent with how the rest of this feature avoids existence leaks).

Write: after every turn, sliding TTL (`ExpiresAt = now + AiSearchOptions.ConversationContextTtlMinutes`,
default 10, configurable). A cleared/expired/failed-reauthorization outcome deletes the row rather
than leaving stale state.

### Classifier prompt & schema changes (`AiClassificationClient`)

Additive only, following the same pattern as `GreetById`'s addition earlier:
- `PersonLookup` added to the known-intents list, with few-shot examples spanning en/hi/hinglish,
  including the exact "Rahul kaun hai?" → "Rahul kya padhate hain?" → "Kaunsi class?" sequence.
- New schema field `languageDirective: "en" | "hi" | null` — set only for an explicit instruction
  ("Hindi mein batao", "reply in English"), never for language that merely *appears* to be Hindi.
- When a `conversation_id` resolves to a stored entity, that entity's name + type is injected into
  the system prompt for this call only, framed unambiguously as context, not as authorization:
  *"The user was just discussing {name}, a {type}. If this message is a natural follow-up about
  that person, resolve it against them."* The classifier is never told anything about scope/auth —
  that remains entirely the backend's job downstream.

### `driver` role (small, piggybacked addition)

- `driver` promoted into `Policies.All` alongside the existing six.
- One new row in `AiIntentAccessRules`: a narrowly-scoped `MyTripStatus` intent (own assigned
  route/trip/stops only), authorized for `driver` alone. No other intent grants `driver` access —
  a driver's AI surface is deliberately the smallest of any role, per your explicit "own assigned
  route/trip only" decision.
- `AiSearchAuthorizationService` gains a `driver` branch analogous to the self-scoped
  `TeacherAttendance`/`StaffAttendance` pattern: resolves the caller's own current trip via
  `ITenantContext.UserId`, never a name/id from the request.

## 5. API Contract Changes

Request gains one optional field:
```json
{ "query": "...", "page": 1, "page_size": 20, "conversation_id": "9f2b3a10-...(optional)" }
```

Response gains one field, always echoed (a fresh id is minted server-side whenever the submitted
one is absent, expired, or foreign — the caller does not need to generate its own):
```json
{ "..." : "...", "conversation_id": "9f2b3a10-..." }
```
Every other existing field (`success`, `language`, `intent`, `answer`, `data`, `page`, `page_size`,
`count`, `has_next_page`, `error`) is unchanged in shape and meaning for every existing intent — this
is a strictly additive contract change.

## 6. Disambiguation Contract

Reuses the existing outcome convention (`intent` as the single outcome axis — no parallel `status`
field, per the reconciliation confirmed in chat) rather than introducing a second one:

```json
{
  "success": true,
  "language": "en",
  "intent": "NeedsClarification",
  "conversation_id": "9f2b3a10-...",
  "answer": "I found two people named Rahul. Which one do you mean?",
  "data": [
    { "name": "Rahul Sharma", "type": "teacher", "detail": "Mathematics" },
    { "name": "Rahul Verma", "type": "student", "detail": "Class 8A" }
  ],
  "page": 1, "page_size": 2, "count": 2, "has_next_page": false
}
```
`data` contains strictly `{name, type, detail}` — no ids, no fields beyond what's needed to tell two
authorized-and-visible people apart. The candidate set backing this response is stored server-side
(§4) so the immediate follow-up ("the teacher" / re-stating the name / a bare "Rahul Sharma") resolves
against just those two candidates, re-authorized fresh, not a new open-ended search.

Neither existing factory fits this shape exactly: `Ok()` requires non-nullable `page`/`pageSize`
(fine here — `page: 1, pageSize: candidates.Count` is the natural reading, no real pagination concept
applies to a 2-5 item disambiguation list) but assumes a "found N, showing M" list semantics that
doesn't quite apply; `Terminal()` hardcodes `count: 0` and nulls `data`, neither of which fits a
clarification that must carry real candidates and a real count. The implementation plan should add
a small `AiSearchResponse.NeedsClarification(language, candidates)` factory alongside the existing
three, rather than force-fitting one of the current two.

## 7. Language Handling

Per-turn detection is unchanged (a real Claude call already handles en/hi/hinglish paraphrase
variety). New: `languageDirective` (§4) is written to `LanguageOverride` on the context row when
present, and every subsequent turn on that `conversation_id` uses the override for its response
language — regardless of what language that turn's own short phrase looks like — until a new
directive arrives or the context expires (at which point language detection reverts to per-turn,
exactly like a fresh conversation).

## 8. Context Security & Expiry — restated as explicit rules

1. `conversation_id` is never checked before, or instead of, `AiSearchAuthorizationService`. It is
   consulted strictly *after* a fresh authorization result exists for the current turn.
2. A stored `(ResolvedEntityId, ResolvedEntityType)` is a **hint**, never a fact. Before it is used
   to answer anything, the handler verifies the entity is still inside the just-computed scope
   (`AllowedChildStudentIds` for parent, `AllowedClassNames`-derived membership for teacher,
   always-true for `Unrestricted` callers). If it fails this check — the entity moved classes, the
   parent-child link was severed, the caller's role changed — the turn fails exactly like a cold
   no-match, and the stale row is deleted, not left to be retried against later.
3. `conversation_id` scoped strictly to `(TenantId, UserId)` — a foreign id (wrong tenant, wrong
   user, or simply invented) is silently treated as absent. No error, no existence signal.
3. TTL is sliding, configurable (`AiSearchOptions.ConversationContextTtlMinutes`, default 10),
   renewed on every successful turn; expired context is silently treated as absent, same as a
   foreign one — never resurrected, never partially trusted.
4. A pending clarification is single-use: a follow-up that doesn't relate to it is classified fresh,
   not trapped waiting for an answer to the disambiguation question.

## 9. Testing

Mirrors the rigor already established by `AiSearchSecurityTests.cs`/`GreetByIdHandlerTests.cs` —
every negative test seeds the actual leak-risk data and asserts both the safe outcome and the
explicit absence of the unsafe one, not just "returns 200."

- **`PersonResolver` unit/integration**: parent fan-out never reaches `Teachers`/`Staff`/`Users`;
  teacher fan-out scoped via Grade+Section (not label-compaction) exactly like the `GreetById` fix;
  admin/owner/principal/staff fan-out is genuinely tenant-scoped (cross-tenant same-name test).
- **Disambiguation**: two same-named people in a caller's authorized scope → `NeedsClarification`
  with exactly the safe fields, no ids in the raw response text; the follow-up resolves against the
  stored candidate set; a follow-up unrelated to the clarification is treated as fresh.
- **Context re-authorization (the security-critical suite)**:
  - Teacher had access to a student who then changes class → old `conversation_id`'s follow-up fails
    closed, no leak of the student's new class or the fact they moved.
  - Parent-child link removed between turns → follow-up fails closed.
  - Caller's role itself changes between turns → follow-up re-authorizes against the new role, not
    the old one.
  - A `conversation_id` from tenant A submitted by a caller in tenant B → treated as absent, silently.
  - A `conversation_id` from user A submitted by user B in the same tenant → treated as absent.
- **TTL**: sliding renewal confirmed (a fast back-and-forth stays alive past the nominal TTL); an
  idle gap past the TTL is confirmed to fall back to a fresh query, not an error.
- **Language**: explicit directive sticks across turns regardless of subsequent per-turn detection;
  expiry resets to per-turn detection; the full three-language worked example (English, Hindi,
  Hinglish) from your spec is encoded as an end-to-end test per language.
- **`driver` role**: `MyTripStatus` resolves the caller's own trip only; every other existing intent
  (all 14 today) is confirmed still denied to `driver` — a regression guard, since promoting `driver`
  into `Policies.All` must not accidentally widen any existing role check elsewhere in the app that
  enumerates `Policies.All`.
- **No regression**: the full existing `AiSearch`-scoped suite (currently 70 unit / 81 integration)
  passes unchanged — every existing intent's contract, especially the envelope shape, is unaffected
  by the new optional `conversation_id` field.

## 10. Risks & Open Items

- **`Users` search performance/design**: this is the one genuinely new repository query in the
  whole feature (no AI intent has searched `Users` directly before). Needs an index check
  (`Users(TenantId, Name)` or equivalent) before this ships, not assumed.
- **Ambiguous admin/owner/principal disambiguation**: if a tenant has two identically-named
  admins, `detail` has nothing obvious to disambiguate on (unlike a student's class or a teacher's
  department). Likely resolution: fall back to `detail: null` and let the client render "two
  admins named X" — flagging this as a decision for the implementation plan, not resolved here.
- **Classifier prompt size growth**: adding `PersonLookup` few-shot examples across three languages,
  plus the context-injection paragraph, grows an already-substantial system prompt. Worth watching
  Claude's classification latency/cost as intents keep growing — not a blocker, a note for Spec 2.
- **Sliding vs. absolute TTL**: sliding was chosen to match natural conversation pacing; if this
  proves to let a conversation "live forever" under continuous light use in practice, an absolute
  cap (e.g. 30 minutes from first turn regardless of activity) is a one-line addition to revisit
  post-launch, not a redesign.
- **Migration number**: M0161 assumed next-available; per the same risk noted in the original AI
  Search spec, bump if concurrent untracked migration work lands first (check `db/Sms.Migrations/`
  immediately before implementation, not from this document).
