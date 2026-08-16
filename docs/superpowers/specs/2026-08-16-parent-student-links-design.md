# Parent–Student Links (multi-child parent accounts)

## Problem

A parent (guardian) can have more than one child enrolled at the same
school. Today, a parent's login is a single `Users` row whose
`StudentId` column holds **one** admission-number string
(`db/Sms.Migrations/M0001_Foundation_Tables.cs:22`). `Parent_EnsureLogin`
already reuses one `Users` row across siblings when their
`Students.GuardianEmail` matches (sibling-reuse block,
`db/Sms.Migrations/procs/identityparent/Parent_EnsureLogin.sql:60-68`),
but `StudentId` is only ever set once, at first insert — so every
downstream consumer that resolves a parent's child via `Users.StudentId`
(e.g. `StudentBusService.GetMyChildrenBusAsync`,
`src/Sms.Application/Services/Transport/StudentBusService.cs:58-105`)
only ever sees the first-linked child. There is no table today that
models "this parent has these N children," and no way for school staff
to manually attach an existing student to an existing parent account
when contact details don't happen to match (e.g. one child registered
under the father's email, another under the mother's).

The `sms-student` app already has a working parent/child persona
(`ChildProvider`/`useSelectedChild`, `KidSwitcher`) built against a
`GET /parents/me/children` endpoint that does not yet exist on the
backend. `sms-admin` needs a staff-facing "Link Student" action to
create these links manually. Both depend on this spec.

## Schema

New migration:

```csharp
Create.Table("ParentStudentLinks")
    .WithColumn("ParentUserId").AsGuid().NotNullable()   // FK Users.Id
    .WithColumn("StudentId").AsGuid().NotNullable()      // FK Students.Id
    .WithColumn("TenantId").AsGuid().NotNullable()       // RLS scoping, matches Students/Users
    .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
Create.PrimaryKey("PK_ParentStudentLinks").OnTable("ParentStudentLinks")
    .Columns("ParentUserId", "StudentId");
```

Apply the same tenant-isolation RLS security policy pattern used on
`Students`/`Users` (see `M0011_Sis_Students.cs:37-41`).

The same migration backfills existing single-child parents:

```sql
INSERT INTO dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId, CreatedAt)
SELECT u.Id, s.Id, s.TenantId, u.CreatedAt
FROM dbo.Users u
JOIN dbo.Students s ON LOWER(LTRIM(RTRIM(s.AdmissionNo))) = LOWER(LTRIM(RTRIM(u.StudentId)))
WHERE u.StudentId IS NOT NULL;
```

`Users.StudentId` is **not** dropped by this migration — it stays in
place, unused by any new code path, and can be removed in a later
cleanup migration once nothing references it.

## Backend logic changes

**`Parent_EnsureLogin.sql`** gets one addition: after resolving
`@UserId` (whether newly created or an existing row reused via the
sibling-email match), insert the link if it doesn't already exist:

```sql
IF NOT EXISTS (
    SELECT 1 FROM dbo.ParentStudentLinks
    WHERE ParentUserId = @UserId
      AND StudentId = (SELECT Id FROM dbo.Students WHERE AdmissionNo = @AdmissionNo AND TenantId = @TenantId)
)
INSERT dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId)
SELECT @UserId, s.Id, s.TenantId FROM dbo.Students s WHERE s.AdmissionNo = @AdmissionNo AND s.TenantId = @TenantId;
```

This is idempotent — logging in again is a no-op. It preserves today's
"same guardian email → auto-linked siblings" behavior, now correctly
reflected in the link table instead of being silently lost.

**New stored proc** `procs/identityparent/ParentStudentLinks_Link.sql`
handles the staff-initiated link (used by the endpoint in the next
section, matching the style of `Parent_EnsureLogin.sql`):

```sql
CREATE OR ALTER PROCEDURE dbo.ParentStudentLinks_Link
    @ParentUserId uniqueidentifier,
    @StudentId    uniqueidentifier,
    @TenantId     uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Id = @ParentUserId AND u.TenantId = @TenantId)
        THROW 51000, 'parent not found in tenant', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Students s WHERE s.Id = @StudentId AND s.TenantId = @TenantId)
        THROW 51001, 'student not found in tenant', 1;
    IF EXISTS (SELECT 1 FROM dbo.ParentStudentLinks WHERE ParentUserId = @ParentUserId AND StudentId = @StudentId)
        THROW 51002, 'link already exists', 1;

    INSERT dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId)
    VALUES (@ParentUserId, @StudentId, @TenantId);
END
```

The read side (list a parent's children) is a plain parameterized
query in a DAO method, not a proc — matching how most read paths in
this repo work:

- `IAuthDao` (or a new small interface) gets
  `Task<IReadOnlyList<RosterStudentRecord>> ListChildrenAsync(Guid parentUserId, CancellationToken ct)`,
  joining `ParentStudentLinks` → `Students`.
- `StudentBusService.GetMyChildrenBusAsync`
  (`src/Sms.Application/Services/Transport/StudentBusService.cs:58-68`)
  swaps its single `me.StudentId` admission lookup for
  `ListChildrenAsync`, so transport gets multi-child support with no
  further transport-specific changes.

## New endpoints

A new `ParentController`:

```csharp
[Route("v1/parents")]
public sealed class ParentController(IParentLinkService parentLinks) : ApiControllerBase
{
    [HttpGet("me/children")]
    [Authorize(Policy = Policies.StudentOrParent)]
    public async Task<IActionResult> MyChildren(CancellationToken ct) =>
        FromResult(await parentLinks.ListMyChildrenAsync(ct));

    [HttpPost("{parentUserId:guid}/children")]
    [Authorize(Policy = Policies.SchoolAdmin)]
    public async Task<IActionResult> LinkChild(Guid parentUserId, [FromBody] LinkChildRequest req, CancellationToken ct) =>
        FromResult(await parentLinks.LinkChildAsync(parentUserId, req.StudentId, ct));
}
```

- `GET v1/parents/me/children` — resolves `tenant.UserId`, calls
  `ListChildrenAsync`, returns the same `RosterStudentRecord`-shaped
  list `sms-student` is already calling this exact path for
  (`src/services/http/index.ts:365-368` in `sms-student`).
- `POST v1/parents/{parentUserId}/children` — the staff "Link Student"
  action's target. Calls `ParentStudentLinks_Link`; maps its `THROW`s
  to HTTP (see Error handling).
- **No new search endpoint.** `GET v1/students?q=&grade=` already
  searches `Name`, `AdmissionNo`, and `ClassLabel` (which encodes
  "Class-Section", e.g. `"5-A"`) via one `q` parameter
  (`src/Sms.Modules.Sis/Data/StudentRepository.cs:64-71`), already
  reachable under a `SchoolAdmin`-appropriate auth policy. The
  `sms-admin` "Link Student" modal's search box wires directly to this
  existing endpoint.

## Error handling

Duplicate prevention has two layers, but the database is the actual
source of truth: the `PRIMARY KEY (ParentUserId, StudentId)` means even
a race between two staff members linking the same pair concurrently
ends in a UNIQUE-violation (SQL error 2627), not a duplicate row. The
proc's explicit `IF EXISTS THROW 51002` is a friendlier error for the
common (non-race) case. The service layer catches both 51002 and 2627
and maps them to the same `409 Conflict`. Errors 51000/51001
(parent/student not in tenant) map to `404`.

The auto-link insert inside `Parent_EnsureLogin` is a silent
`IF NOT EXISTS` upsert — login must never fail because of a link race,
so there is no error path there.

On the `sms-admin` side, the confirmation-summary step (see the
separate `sms-admin` "Link Student" modal design) is a UX safeguard
against picking the wrong student — it is not the duplicate check
itself. A `409` from the API should surface there as "This student is
already linked to this guardian," not a generic failure.

## Testing

- **Migration test**: apply the migration against a seeded test DB with
  pre-existing `Users.StudentId` values; assert the backfill produces
  exactly one `ParentStudentLinks` row per existing parent-student
  pairing.
- **Proc-level integration test** for `ParentStudentLinks_Link`: happy
  path insert; duplicate → 51002; cross-tenant parent/student →
  51000/51001.
- **Service/controller tests** for `ParentController`: `MyChildren`
  returns all linked children for a multi-child parent; `LinkChild`
  returns 200/404/409 correctly.
- **Update existing `StudentBusService` tests**: `GetMyChildrenBusAsync`
  now resolves via the link table instead of `Users.StudentId` — this
  adjusts an existing test fixture, it is not new test infrastructure.
- The `sms-admin` "Link Student" modal gets its own component/flow
  tests (search → select → confirm → cancel/duplicate-409 handling) as
  part of that separate, follow-on spec — out of scope here.

## Out of scope

- Dropping `Users.StudentId` (left in place for a later cleanup
  migration).
- The `sms-admin` "Link Student" modal UI itself — this spec only
  covers the backend endpoint it calls.
- Un-linking a student from a parent (no requirement surfaced for this
  yet; add as a follow-up if staff need to correct a bad link).
