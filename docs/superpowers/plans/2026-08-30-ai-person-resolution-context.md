# AI Person Resolution & Conversational Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `POST /v1/ai/search` with multi-entity person lookup ("Rahul kaun hai?" resolving
across student/teacher/staff/admin/owner/principal), a `conversation_id`-based follow-up mechanism
that re-authorizes every turn from scratch, a universal `status` field distinct from `intent`, and a
narrowly-scoped `driver` role addition (`MyTripStatus`).

**Architecture:** `AiSearchService.SearchAsync` gains conversation-context load/save around the
existing classification→authorization→dispatch pipeline. `IPersonResolver` fans out across four
existing repositories (`ISisService`, `TeacherRepository`, `StaffRepository`, a new
`IUserDirectoryLookup`), each scoped by the already-existing `AiAuthorizationResult`. A `status` field
is added to every response alongside the unchanged `intent` field. Nothing about the original 20-task
AI Search pipeline or `GreetById`'s authorization model is replaced — this plan extends it.

**Tech Stack:** .NET 10, Dapper (no EF Core), FluentMigrator, xUnit + FluentAssertions, Anthropic
Claude API via `HttpClient` (all matching the existing AI Search feature exactly).

**Spec:** `docs/superpowers/specs/2026-08-30-ai-person-resolution-context-design.md`

## Global Constraints

- Read-only: no new handler may ever call a write-capable repository method.
- No dynamic SQL string-concatenation — every query is Dapper with parameters, per `BaseRepository`.
- `conversation_id` is conversational convenience only, never authorization. Every turn re-runs
  `AiSearchAuthorizationService.AuthorizeAsync` in full; a stored resolved-entity hint is verified
  against the FRESH authorization result before use, never trusted on its own.
- `null`/empty `AllowedChildStudentIds`/`AllowedClassNames` is never "no filter" — only
  `Unrestricted == true` means that. Every new component must honor this.
- `intent` keeps its existing meaning and existing string values for every case, including the three
  existing refusal outcomes (`Forbidden`/`Unsupported`/`WriteBlocked`) — this is a **non-breaking**
  change confirmed with the user; `sms-admin`'s already-merged AI Mode must not need updating.
- `status` is the new outcome field, applied universally: `success`, `needs_clarification`,
  `no_match`, `unsupported`, `write_blocked`, `forbidden`, `error` (7 values — `forbidden` was
  confirmed as an addition beyond the user's original 6).
- Disambiguation `data` contains strictly `{name, type, detail}` — never an id, never any field
  beyond what's needed to tell two authorized-and-visible people apart.
- Migration numbers: M0161 (`AiSearchConversation`) and M0162 (`Users` index) are next-available
  after M0160 as of plan-writing — run `ls db/Sms.Migrations/*.cs | grep -oE "M[0-9]{4}" | sort | tail -1`
  before Task 1 and bump both numbers together if something else has landed first.
- `dbo.Users.Name` **already exists** (added in `M0084_Identity_Link_Foundation`, already merged) —
  do not attempt to add this column again. It has no supporting index and no proc populates it today;
  this plan adds the index only and accepts that unnamed accounts simply won't be found by name.

---

### Task 1: `AiSearchConversation` table + `IAiConversationContextStore`

**Files:**
- Create: `db/Sms.Migrations/M0161_AiSearchConversation.cs`
- Create: `src/Sms.Modules.AiSearch/Data/AiSearchConversationRepository.cs`
- Create: `src/Sms.Application/Services/AiSearch/AiConversationContextStore.cs`
- Modify: `src/Sms.Shared.Kernel/AiSearch/AiSearchOptions.cs`
- Modify: `src/Sms.Modules.AiSearch/AiSearchModule.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/AiConversationContextStoreTests.cs`

**Interfaces:**
- Produces: `IAiConversationContextStore.LoadAsync(Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct) : Task<AiConversationContext?>`,
  `SaveAsync(Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct) : Task<Guid>` (returns the effective conversation id — new or reused),
  `ClearAsync(Guid conversationId, CancellationToken ct) : Task`.
  `AiConversationContext(Guid? ResolvedEntityId, string? ResolvedEntityType, string? LanguageOverride, IReadOnlyList<PendingCandidate>? PendingCandidates, string? LastIntent)`,
  `PendingCandidate(Guid Id, string Type)`.

- [ ] **Step 1: Find the current migration number**

Run: `ls db/Sms.Migrations/*.cs | grep -oE "M[0-9]{4}" | sort | tail -1`

If it's higher than `M0160`, use `N+1` in place of `M0161` throughout this task and Task 2.

- [ ] **Step 2: Write the migration**

Create `db/Sms.Migrations/M0161_AiSearchConversation.cs`:

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(161, "AiSearchConversation: short-lived conversational context for AI person lookup follow-ups")]
public sealed class M0161_AiSearchConversation : Migration
{
    public override void Up()
    {
        Create.Table("AiSearchConversation")
            .WithColumn("ConversationId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("ResolvedEntityId").AsGuid().Nullable()
            .WithColumn("ResolvedEntityType").AsString(20).Nullable()
            .WithColumn("LanguageOverride").AsString(10).Nullable()
            .WithColumn("PendingCandidates").AsString(int.MaxValue).Nullable()
            .WithColumn("LastIntent").AsString(60).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable();

        Create.Index("IX_AiSearchConversation_Expiry")
            .OnTable("AiSearchConversation")
            .OnColumn("ExpiresAt").Ascending();
    }

    public override void Down()
    {
        Delete.Table("AiSearchConversation");
    }
}
```

- [ ] **Step 3: Add the TTL config options**

In `src/Sms.Shared.Kernel/AiSearch/AiSearchOptions.cs`, add two properties to the existing class:

```csharp
    public int ConversationContextTtlMinutes { get; set; } = 10;
    public int ConversationContextAbsoluteMaxMinutes { get; set; } = 30;
```

- [ ] **Step 4: Write the repository**

Create `src/Sms.Modules.AiSearch/Data/AiSearchConversationRepository.cs`:

```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.AiSearch.Data;

public sealed record AiSearchConversationRow(
    Guid ConversationId, Guid TenantId, Guid UserId, Guid? ResolvedEntityId, string? ResolvedEntityType,
    string? LanguageOverride, string? PendingCandidates, string? LastIntent, DateTime CreatedAt, DateTime ExpiresAt);

public sealed class AiSearchConversationRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<AiSearchConversationRow?> FindAsync(
        Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<AiSearchConversationRow>(
            @"SELECT ConversationId, TenantId, UserId, ResolvedEntityId, ResolvedEntityType,
                     LanguageOverride, PendingCandidates, LastIntent, CreatedAt, ExpiresAt
              FROM dbo.AiSearchConversation
              WHERE ConversationId = @conversationId AND TenantId = @tenantId AND UserId = @userId",
            new { conversationId, tenantId, userId }, ct);
        return rows.FirstOrDefault();
    }

    public Task UpsertAsync(AiSearchConversationRow row, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            @"MERGE dbo.AiSearchConversation AS target
              USING (SELECT @ConversationId AS ConversationId) AS src
              ON target.ConversationId = src.ConversationId
              WHEN MATCHED THEN UPDATE SET
                  ResolvedEntityId = @ResolvedEntityId, ResolvedEntityType = @ResolvedEntityType,
                  LanguageOverride = @LanguageOverride, PendingCandidates = @PendingCandidates,
                  LastIntent = @LastIntent, ExpiresAt = @ExpiresAt
              WHEN NOT MATCHED THEN INSERT
                  (ConversationId, TenantId, UserId, ResolvedEntityId, ResolvedEntityType,
                   LanguageOverride, PendingCandidates, LastIntent, CreatedAt, ExpiresAt)
                  VALUES (@ConversationId, @TenantId, @UserId, @ResolvedEntityId, @ResolvedEntityType,
                          @LanguageOverride, @PendingCandidates, @LastIntent, @CreatedAt, @ExpiresAt);",
            row, ct);

    public Task DeleteAsync(Guid conversationId, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            "DELETE FROM dbo.AiSearchConversation WHERE ConversationId = @conversationId",
            new { conversationId }, ct);
}
```

- [ ] **Step 5: Register the repository in `AiSearchModule`**

In `src/Sms.Modules.AiSearch/AiSearchModule.cs`, add inside `AddAiSearchModule`:

```csharp
        services.AddScoped<AiSearchConversationRepository>();
```

- [ ] **Step 6: Write the failing test for the store's expiry logic**

Create `tests/Sms.Tests.Integration/AiSearch/AiConversationContextStoreTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sms.Application.Services.AiSearch;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.AiSearch;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class AiConversationContextStoreTests(SqlServerFixture fx)
{
    private static AiConversationContextStore MakeStore(FakeTimeProvider clock, int ttlMin = 10, int absMaxMin = 30)
    {
        var factory = new SqlConnectionFactory(fx.ConnectionString, new NoOpTenantContext());
        var repo = new AiSearchConversationRepository(factory);
        var options = Options.Create(new AiSearchOptions
        {
            ConversationContextTtlMinutes = ttlMin, ConversationContextAbsoluteMaxMinutes = absMaxMin
        });
        return new AiConversationContextStore(repo, options, clock);
    }

    [Fact]
    public async Task Save_then_load_round_trips_the_resolved_entity()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = MakeStore(clock);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var conversationId = await store.SaveAsync(null, tenantId, userId,
            new AiConversationContext(entityId, "teacher", null, null, "PersonLookup"));
        var loaded = await store.LoadAsync(conversationId, tenantId, userId);

        loaded.Should().NotBeNull();
        loaded!.ResolvedEntityId.Should().Be(entityId);
        loaded.ResolvedEntityType.Should().Be("teacher");
    }

    [Fact]
    public async Task Load_after_the_sliding_TTL_with_no_activity_returns_null()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = MakeStore(clock, ttlMin: 10);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var conversationId = await store.SaveAsync(null, tenantId, userId,
            new AiConversationContext(Guid.NewGuid(), "student", null, null, "PersonLookup"));

        clock.Advance(TimeSpan.FromMinutes(11));

        (await store.LoadAsync(conversationId, tenantId, userId)).Should().BeNull();
    }

    [Fact]
    public async Task Sliding_renewal_keeps_a_fast_back_and_forth_alive_past_the_nominal_TTL()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = MakeStore(clock, ttlMin: 10, absMaxMin: 30);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var conversationId = await store.SaveAsync(null, tenantId, userId,
            new AiConversationContext(entityId, "student", null, null, "PersonLookup"));

        // Two more turns, 8 minutes apart each (inside the 10-min sliding window each time).
        clock.Advance(TimeSpan.FromMinutes(8));
        (await store.LoadAsync(conversationId, tenantId, userId)).Should().NotBeNull();
        await store.SaveAsync(conversationId, tenantId, userId,
            new AiConversationContext(entityId, "student", null, null, "PersonLookup"));

        clock.Advance(TimeSpan.FromMinutes(8));
        var stillAlive = await store.LoadAsync(conversationId, tenantId, userId);
        stillAlive.Should().NotBeNull("16 minutes have passed in total, past the 10-minute nominal TTL, but each turn was inside it");
    }

    [Fact]
    public async Task Absolute_cap_ends_the_conversation_even_under_continuous_activity()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = MakeStore(clock, ttlMin: 10, absMaxMin: 30);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var conversationId = await store.SaveAsync(null, tenantId, userId,
            new AiConversationContext(entityId, "student", null, null, "PersonLookup"));

        // Renew every 8 minutes (always inside the sliding TTL) for 32 minutes total -- past the
        // 30-minute absolute cap anchored to CreatedAt.
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(8));
            await store.SaveAsync(conversationId, tenantId, userId,
                new AiConversationContext(entityId, "student", null, null, "PersonLookup"));
        }

        (await store.LoadAsync(conversationId, tenantId, userId)).Should().BeNull(
            "the absolute cap must end the conversation regardless of continuous renewal");
    }

    [Fact]
    public async Task A_conversation_id_belonging_to_a_different_user_is_never_returned()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = MakeStore(clock);
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var conversationId = await store.SaveAsync(null, tenantId, ownerUserId,
            new AiConversationContext(Guid.NewGuid(), "student", null, null, "PersonLookup"));

        (await store.LoadAsync(conversationId, tenantId, otherUserId)).Should().BeNull();
    }
}
```

Check `tests/Sms.Tests.Integration/` for an existing `FakeTimeProvider`/`NoOpTenantContext` test double
(e.g. grep for `TimeProvider` usage in `DailyAttendanceSummaryHandlerTests.cs` or similar) — reuse
whatever this codebase already has rather than writing a new one. If none exists, add a minimal
`FakeTimeProvider : TimeProvider` with a settable `DateTimeOffset` and an `Advance(TimeSpan)` method,
overriding `GetUtcNow()`.

- [ ] **Step 7: Run the tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter AiConversationContextStoreTests -v n`
Expected: FAIL — `AiConversationContextStore` does not exist yet.

- [ ] **Step 8: Implement `AiConversationContextStore`**

Create `src/Sms.Application/Services/AiSearch/AiConversationContextStore.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.AiSearch;

namespace Sms.Application.Services.AiSearch;

public sealed record PendingCandidate(Guid Id, string Type);

public sealed record AiConversationContext(
    Guid? ResolvedEntityId, string? ResolvedEntityType, string? LanguageOverride,
    IReadOnlyList<PendingCandidate>? PendingCandidates, string? LastIntent);

public interface IAiConversationContextStore
{
    Task<AiConversationContext?> LoadAsync(Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<Guid> SaveAsync(Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default);
    Task ClearAsync(Guid conversationId, CancellationToken ct = default);
}

/// <summary>
/// conversation_id is a conversational convenience ONLY -- see AiSearchAuthorizationService's own
/// doc comments for the authorization invariant this store must never violate. This class is
/// deliberately dumb: it stores and expires a hint. Nothing here makes an authorization decision.
/// </summary>
public sealed class AiConversationContextStore(
    AiSearchConversationRepository repo, IOptions<AiSearchOptions> options, TimeProvider clock)
    : IAiConversationContextStore
{
    public async Task<AiConversationContext?> LoadAsync(
        Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var row = await repo.FindAsync(conversationId, tenantId, userId, ct);
        if (row is null) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var absoluteDeadline = row.CreatedAt.AddMinutes(options.Value.ConversationContextAbsoluteMaxMinutes);
        if (now >= row.ExpiresAt || now >= absoluteDeadline)
        {
            await repo.DeleteAsync(conversationId, ct);
            return null;
        }

        var candidates = string.IsNullOrWhiteSpace(row.PendingCandidates)
            ? null
            : JsonSerializer.Deserialize<List<PendingCandidate>>(row.PendingCandidates);

        return new AiConversationContext(
            row.ResolvedEntityId, row.ResolvedEntityType, row.LanguageOverride, candidates, row.LastIntent);
    }

    public async Task<Guid> SaveAsync(
        Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default)
    {
        var id = conversationId ?? Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;

        // CreatedAt must be preserved across renewals for the absolute cap to mean anything -- only
        // set it to "now" for a genuinely new conversation id. A renewal's CreatedAt is read back
        // from the existing row where possible; falling back to "now" for a caller-supplied id this
        // store has never seen is the safe default (a fresh conversation, not an error).
        var existing = conversationId is { } existingId ? await repo.FindAsync(existingId, tenantId, userId, ct) : null;
        var createdAt = existing?.CreatedAt ?? now;

        await repo.UpsertAsync(new AiSearchConversationRow(
            id, tenantId, userId, context.ResolvedEntityId, context.ResolvedEntityType,
            context.LanguageOverride,
            context.PendingCandidates is null ? null : JsonSerializer.Serialize(context.PendingCandidates),
            context.LastIntent, createdAt, now.AddMinutes(options.Value.ConversationContextTtlMinutes)), ct);

        return id;
    }

    public Task ClearAsync(Guid conversationId, CancellationToken ct = default) => repo.DeleteAsync(conversationId, ct);
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter AiConversationContextStoreTests -v n`
Expected: PASS (all 5 cases)

- [ ] **Step 10: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, near the other `AiSearch` registrations:

```csharp
builder.Services.AddScoped<IAiConversationContextStore, AiConversationContextStore>();
```

- [ ] **Step 11: Commit**

```bash
git add db/Sms.Migrations/M0161_AiSearchConversation.cs src/Sms.Modules.AiSearch/Data/AiSearchConversationRepository.cs src/Sms.Application/Services/AiSearch/AiConversationContextStore.cs src/Sms.Shared.Kernel/AiSearch/AiSearchOptions.cs src/Sms.Modules.AiSearch/AiSearchModule.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/AiSearch/AiConversationContextStoreTests.cs
git commit -m "feat(ai-search): add AiSearchConversation table and context store with sliding+absolute TTL"
```

---

### Task 2: `Users` name index + `IUserDirectoryLookup`

**Files:**
- Create: `db/Sms.Migrations/M0162_Users_TenantNameIndex.cs`
- Create: `src/Sms.Shared.Kernel/Auth/UserDirectoryRepository.cs`
- Test: `tests/Sms.Tests.Integration/Auth/UserDirectoryRepositoryTests.cs`

**Interfaces:**
- Produces: `UserDirectoryMatch(Guid Id, string Name, string Type, string? Email)` (`Type`: `"admin"` | `"owner"` | `"principal"`),
  `IUserDirectoryLookup.SearchByNameAsync(string name, CancellationToken ct) : Task<IReadOnlyList<UserDirectoryMatch>>`.

- [ ] **Step 1: Write the migration**

Create `db/Sms.Migrations/M0162_Users_TenantNameIndex.cs` (bump the number if Task 1's migration
number changed):

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(162, "Users: add (TenantId, Name) index for AI person-lookup search - Users.Name column already exists since M0084")]
public sealed class M0162_Users_TenantNameIndex : Migration
{
    public override void Up()
    {
        Create.Index("IX_Users_Tenant_Name")
            .OnTable("Users")
            .OnColumn("TenantId").Ascending()
            .OnColumn("Name").Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_Users_Tenant_Name").OnTable("Users");
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Sms.Tests.Integration/Auth/UserDirectoryRepositoryTests.cs`. Check
`tests/Sms.Tests.Integration/` for how other tests seed a `Users` row with roles directly via SQL
(e.g. `AiSearchSecurityTests.cs`'s `Seed`/`Data` helpers, or however `TestTenancy` seeds accounts) and
match that exact pattern rather than inventing new seeding:

```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class UserDirectoryRepositoryTests(SqlServerFixture fx)
{
    [Fact]
    public async Task SearchByNameAsync_finds_an_admin_by_name_and_reports_the_role_as_type()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.Users (Id, TenantId, Email, Name, Status) VALUES (@userId, @tenantId, @email, @name, 'active')",
            new { userId, tenantId, email = $"owner{Guid.NewGuid():N}@school.test", name = "Rahul Sharma" }));
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.owner')", new { userId }));

        var factory = new SqlConnectionFactory(fx.ConnectionString, new TestTenantContext(tenantId));
        var repo = new UserDirectoryRepository(factory);

        var matches = await repo.SearchByNameAsync("Rahul");

        matches.Should().ContainSingle(m => m.Id == userId && m.Name == "Rahul Sharma" && m.Type == "owner");
    }

    [Fact]
    public async Task SearchByNameAsync_never_matches_a_row_with_no_Name_set()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.Users (Id, TenantId, Email, Status) VALUES (@userId, @tenantId, @email, 'active')",
            new { userId, tenantId, email = $"noname{Guid.NewGuid():N}@school.test" }));
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.admin')", new { userId }));

        var factory = new SqlConnectionFactory(fx.ConnectionString, new TestTenantContext(tenantId));
        var repo = new UserDirectoryRepository(factory);

        (await repo.SearchByNameAsync("anything")).Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByNameAsync_never_returns_a_student_parent_or_teacher_role_only_account()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.Users (Id, TenantId, Email, Name, Status) VALUES (@userId, @tenantId, @email, 'Rahul Sharma', 'active')",
            new { userId, tenantId, email = $"teacher{Guid.NewGuid():N}@school.test" }));
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.teacher')", new { userId }));

        var factory = new SqlConnectionFactory(fx.ConnectionString, new TestTenantContext(tenantId));
        var repo = new UserDirectoryRepository(factory);

        (await repo.SearchByNameAsync("Rahul")).Should().BeEmpty(
            "a school.teacher-only account is directory data for PersonResolver's Teachers-table branch, not the Users branch");
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_current_name_for_a_known_admin_like_id()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.Users (Id, TenantId, Email, Name, Status) VALUES (@userId, @tenantId, @email, @name, 'active')",
            new { userId, tenantId, email = $"principal{Guid.NewGuid():N}@school.test", name = "Priya Singh" }));
        await Query(conn => conn.ExecuteAsync(
            "INSERT INTO dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.principal')", new { userId }));

        var factory = new SqlConnectionFactory(fx.ConnectionString, new TestTenantContext(tenantId));
        var repo = new UserDirectoryRepository(factory);

        var match = await repo.GetByIdAsync(userId);

        match.Should().NotBeNull();
        match!.Name.Should().Be("Priya Singh");
        match.Type.Should().Be("principal");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_an_unknown_id()
    {
        var tenantId = Guid.NewGuid();
        var factory = new SqlConnectionFactory(fx.ConnectionString, new TestTenantContext(tenantId));
        var repo = new UserDirectoryRepository(factory);

        (await repo.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static Task<T> Query<T>(Func<System.Data.IDbConnection, Task<T>> f) => fx.QueryAsync(f);
}
```

Check the existing `SqlServerFixture` API (`fx.QueryAsync` or similar) used by other integration tests
in this project for the exact helper name/signature and adjust the private `Query` helper above to
match — do not invent a new fixture API. Also check for an existing `TestTenantContext`-style
`ITenantContext` test double used elsewhere in the integration suite (used already by earlier AiSearch
tests) and reuse it rather than writing a new one.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter UserDirectoryRepositoryTests -v n`
Expected: FAIL — `UserDirectoryRepository` does not exist.

- [ ] **Step 4: Implement the repository**

Create `src/Sms.Shared.Kernel/Auth/UserDirectoryRepository.cs`:

```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed record UserDirectoryMatch(Guid Id, string Name, string Type, string? Email);

public interface IUserDirectoryLookup
{
    Task<IReadOnlyList<UserDirectoryMatch>> SearchByNameAsync(string name, CancellationToken ct = default);

    /// Single-id lookup, used to re-fetch a previously-resolved admin/owner/principal's CURRENT name
    /// on a conversation follow-up (Task 12) -- never trust a name carried in from prior context,
    /// always re-read it fresh at the point of use.
    Task<UserDirectoryMatch?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Searches dbo.Users by name for admin/owner/principal person-lookup only -- never for
/// parent/student/teacher/staff-only accounts, which are directory data for PersonResolver's other
/// three sources. Relies on the same RLS/ITenantContext session-scoping every other repository in
/// this codebase already gets from IDbConnectionFactory -- no manual TenantId filter is written here,
/// matching convention.
/// <para>
/// Role priority when a single Users row carries more than one of the three admin-like roles (rare,
/// e.g. an owner who is also flagged principal): owner &gt; principal &gt; admin, an arbitrary but
/// defined, documented order -- never ambiguous about which Type a match reports.
/// </para>
/// </summary>
public sealed class UserDirectoryRepository(IDbConnectionFactory factory) : BaseRepository(factory)
    : IUserDirectoryLookup
{
    public async Task<IReadOnlyList<UserDirectoryMatch>> SearchByNameAsync(string name, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<(Guid Id, string Name, string? Email, string Roles)>(
            @"SELECT u.Id, u.Name, u.Email,
                     Roles = STRING_AGG(ur.Role, ',') WITHIN GROUP (ORDER BY ur.Role)
              FROM dbo.Users u
              JOIN dbo.UserRoles ur ON ur.UserId = u.Id
              WHERE u.Name IS NOT NULL
                AND u.Name LIKE '%' + @name + '%'
                AND ur.Role IN ('school.owner', 'school.principal', 'school.admin')
              GROUP BY u.Id, u.Name, u.Email",
            new { name }, ct);

        return rows.Select(r => new UserDirectoryMatch(r.Id, r.Name, TypeFor(r.Roles), r.Email)).ToList();
    }

    public async Task<UserDirectoryMatch?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<(Guid Id, string Name, string? Email, string Roles)>(
            @"SELECT u.Id, u.Name, u.Email,
                     Roles = STRING_AGG(ur.Role, ',') WITHIN GROUP (ORDER BY ur.Role)
              FROM dbo.Users u
              JOIN dbo.UserRoles ur ON ur.UserId = u.Id
              WHERE u.Id = @id AND u.Name IS NOT NULL
                AND ur.Role IN ('school.owner', 'school.principal', 'school.admin')
              GROUP BY u.Id, u.Name, u.Email",
            new { id }, ct);
        var row = rows.FirstOrDefault();
        return row.Id == Guid.Empty ? null : new UserDirectoryMatch(row.Id, row.Name, TypeFor(row.Roles), row.Email);
    }

    private static string TypeFor(string roles) =>
        roles.Contains("school.owner") ? "owner" :
        roles.Contains("school.principal") ? "principal" : "admin";
}
```

Note the class declaration line wraps for readability in this document — write it as valid C# on one
or two lines (`public sealed class UserDirectoryRepository(IDbConnectionFactory factory) : BaseRepository(factory), IUserDirectoryLookup`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter UserDirectoryRepositoryTests -v n`
Expected: PASS (all 5 cases)

- [ ] **Step 6: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`:

```csharp
builder.Services.AddScoped<IUserDirectoryLookup, UserDirectoryRepository>();
```

- [ ] **Step 7: Commit**

```bash
git add db/Sms.Migrations/M0162_Users_TenantNameIndex.cs src/Sms.Shared.Kernel/Auth/UserDirectoryRepository.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Auth/UserDirectoryRepositoryTests.cs
git commit -m "feat(ai-search): add Users(TenantId,Name) index and IUserDirectoryLookup for admin/owner/principal search"
```

---

### Task 3: `AiSearchResponse` gains `Status`, `ConversationId`, `ConversationUpdate`, `NeedsClarification`

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/AiSearchModels.cs`
- Test: `tests/Sms.Tests.Unit/AiSearch/AiSearchResponseTests.cs`

**Interfaces:**
- Produces: `AiSearchResponse` gains `Status` (string, always set) and `ConversationId` (string?,
  serialized) plus a non-serialized `ConversationUpdate` property (internal signal from a handler to
  the orchestrator — see rationale below); `PersonCandidate(string Name, string Type, string? Detail)`;
  `AiConversationUpdate(Guid? ResolvedEntityId, string? ResolvedEntityType, IReadOnlyList<PendingCandidate>? PendingCandidates)`;
  `AiSearchResponse.NeedsClarification(string language, string intent, string answer, IReadOnlyList<PersonCandidate> candidates)`.

**Design note carried from the spec:** the wire-facing `data` field for a clarification must never
contain an id (§6 of the spec) — but the orchestrator still needs the *real* candidate ids to persist
`PendingCandidates` for the follow-up to resolve against. `ConversationUpdate` is the answer: a
`[JsonIgnore]` property on `AiSearchResponse` that a handler sets internally (never serialized to the
client) carrying exactly what the orchestrator needs to write to the conversation-context store. Every
other existing handler simply leaves it `null`, which the orchestrator treats as "nothing to persist
for this turn beyond renewing the TTL."

- [ ] **Step 1: Write the failing tests**

Create `tests/Sms.Tests.Unit/AiSearch/AiSearchResponseTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiSearchResponseTests
{
    [Fact]
    public void Ok_sets_status_to_success()
    {
        var response = AiSearchResponse.Ok("en", "StudentSearch", "ok", null, 1, 20, 0, false);
        response.Status.Should().Be("success");
    }

    [Fact]
    public void Terminal_uses_the_caller_supplied_status()
    {
        var response = AiSearchResponse.Terminal("en", "Forbidden", "no", "forbidden");
        response.Status.Should().Be("forbidden");
        response.Intent.Should().Be("Forbidden", "intent keeps its existing meaning unchanged - non-breaking");
    }

    [Fact]
    public void Fail_sets_status_to_error()
    {
        var response = AiSearchResponse.Fail("InvalidRequest", "bad");
        response.Status.Should().Be("error");
    }

    [Fact]
    public void NeedsClarification_carries_candidates_with_a_real_count_and_no_ids_in_the_serialized_data()
    {
        var candidates = new[]
        {
            new PersonCandidate("Rahul Sharma", "teacher", "Mathematics"),
            new PersonCandidate("Rahul Verma", "student", "Class 8A"),
        };
        var response = AiSearchResponse.NeedsClarification(
            "en", "PersonLookup", "I found two people named Rahul. Which one do you mean?", candidates);

        response.Status.Should().Be("needs_clarification");
        response.Intent.Should().Be("PersonLookup");
        response.Count.Should().Be(2);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        json.Should().NotContain("Guid").And.NotMatch("*id*:*-*-*-*-*"); // no GUID-shaped id anywhere in the payload
    }

    [Fact]
    public void ConversationUpdate_is_never_serialized()
    {
        var response = AiSearchResponse.Ok("en", "PersonLookup", "x", null, 1, 1, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(Guid.NewGuid(), "teacher", null) };

        var json = JsonSerializer.Serialize(response);
        json.Should().NotContain("ConversationUpdate").And.NotContain("conversation_update");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiSearchResponseTests -v n`
Expected: FAIL — `Status` does not exist yet, `Terminal`/`NeedsClarification` signatures don't match.

- [ ] **Step 3: Rewrite `AiSearchModels.cs`**

Replace the contents of `src/Sms.Application/Services/AiSearch/AiSearchModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Sms.Application.Services.AiSearch;

public sealed record AiSearchRequest(string Query, int? Page, int? PageSize, string? ConversationId);

public sealed record AiSearchFilters(
    string? StudentName,
    string? ClassName,
    string? Section,
    string? DateExpression,
    bool TargetSelf);

public sealed record AiClassificationResult(string Language, string Intent, AiSearchFilters Filters, string? LanguageDirective = null);

public sealed record AiSearchError(string Code, string Message);

public sealed record PersonCandidate(string Name, string Type, string? Detail);

public sealed record PendingCandidate(Guid Id, string Type);

/// <summary>
/// A handler's internal-only signal to the orchestrator about what to persist to the conversation
/// context store for this turn. Never serialized to the client -- see AiSearchResponse.ConversationUpdate.
/// Every handler except PersonLookupHandler leaves this null.
/// </summary>
public sealed record AiConversationUpdate(
    Guid? ResolvedEntityId, string? ResolvedEntityType, IReadOnlyList<PendingCandidate>? PendingCandidates);

public sealed record AiSearchResponse(
    bool Success,
    string? Language,
    string? Intent,
    string Status,
    string? Answer,
    object? Data,
    int? Page,
    int? PageSize,
    int? Count,
    bool? HasNextPage,
    AiSearchError? Error)
{
    /// Echoed back to the caller by the controller; set by the orchestrator after a handler runs,
    /// never by a handler itself (handlers don't know or manage the conversation id).
    public string? ConversationId { get; init; }

    [JsonIgnore]
    public AiConversationUpdate? ConversationUpdate { get; init; }

    public static AiSearchResponse Ok(
        string language, string intent, string answer, object? data,
        int page, int pageSize, int count, bool hasNextPage) =>
        new(true, language, intent, "success", answer, data, page, pageSize, count, hasNextPage, null);

    public static AiSearchResponse Terminal(string language, string intent, string answer, string status) =>
        new(true, language, intent, status, answer, null, null, null, 0, false, null);

    public static AiSearchResponse Fail(string code, string message) =>
        new(false, null, null, "error", null, null, null, null, null, null, new AiSearchError(code, message));

    public static AiSearchResponse NeedsClarification(
        string language, string intent, string answer, IReadOnlyList<PersonCandidate> candidates) =>
        new(true, language, intent, "needs_clarification", answer, candidates,
            1, candidates.Count, candidates.Count, false, null);
}
```

Note: `AiSearchRequest` gains a fourth field, `ConversationId` — this is a breaking constructor-arity
change to a record used throughout the existing codebase (controller model binding, every existing
test that constructs one directly). Grep for every `new AiSearchRequest(` call site
(`grep -rn "new AiSearchRequest(" tests/ src/`) and add `null` as the fourth argument at each one,
UNLESS that call site is specifically testing the new field. The controller itself binds this from
JSON automatically (snake_case `conversation_id`) and needs no code change for the binding itself.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiSearchResponseTests -v n`
Expected: PASS

- [ ] **Step 5: Fix every existing call site that breaks from the constructor changes**

Run: `dotnet build src/Sms.Api -c Debug --nologo` and fix every resulting compile error. Two patterns
you'll see:
- `new AiSearchRequest(query, page, pageSize)` → add a fourth `null` (or the real conversation id, if
  the test is specifically about it): `new AiSearchRequest(query, page, pageSize, null)`.
- Any call to `AiSearchResponse.Terminal(language, intent, answer)` (3 args) now fails to compile —
  **do not fix these here**; Task 4 is entirely dedicated to retrofitting every `Terminal(...)` call
  site with the correct 4th `status` argument. For this task, it is acceptable (and expected) that
  `dotnet build` still fails on every `Terminal(...)` 3-arg call site — confirm the ONLY remaining
  build errors after this step are `Terminal(...)` arity errors, nothing else.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/AiSearchModels.cs tests/Sms.Tests.Unit/AiSearch/AiSearchResponseTests.cs
git commit -m "feat(ai-search): add Status/ConversationId/ConversationUpdate to AiSearchResponse, NeedsClarification factory"
```

Note this commit intentionally leaves the build broken at every `Terminal(...)` 3-arg call site —
Task 4 fixes every one of them in one pass, with tests, and is the commit that restores a green build.

---

### Task 4: Retrofit every existing `Terminal(...)` call site with the correct `status`

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/AiSearchService.cs`
- Modify: every file under `src/Sms.Application/Services/AiSearch/Handlers/` that calls `AiSearchResponse.Terminal(...)`
- Test: existing tests in `tests/Sms.Tests.Unit/AiSearch/` and `tests/Sms.Tests.Integration/AiSearch/` (updated, not new)

**Interfaces:**
- Consumes: `AiSearchResponse.Terminal(string language, string intent, string answer, string status)` (Task 3).

**The rule, applied identically everywhere — no exceptions:**
- In `AiSearchService.cs`'s own `TerminalAsync` helper (the orchestrator-level refusals): the outcome
  IS the status, lowercased with underscores — `"Forbidden"` → `status: "forbidden"`,
  `"Unsupported"` → `status: "unsupported"`, `"WriteBlocked"` → `status: "write_blocked"`.
- In every **handler's own** `Terminal(language, "Unsupported", ...)` call (a handler saying "I
  couldn't find/resolve what you asked for", as opposed to the orchestrator saying "no handler exists
  for this intent at all") — status is `"no_match"`, not `"unsupported"`. This is the one real
  semantic split this retrofit introduces: today both cases share the wire string `"Unsupported"` for
  `intent`; after this task they still share `intent: "Unsupported"` (non-breaking, per the Global
  Constraints) but their `status` values now differ, correctly distinguishing "the backend doesn't
  support this at all" from "this specific lookup found nothing."

- [ ] **Step 1: Find every call site**

Run: `grep -rn "AiSearchResponse.Terminal(" src/Sms.Application/Services/AiSearch/`

You will find calls in `AiSearchService.cs` (the orchestrator's own `TerminalAsync` — do not touch
that helper's callers, fix the helper itself, see Step 2) and in most files under
`Handlers/` (`ClassAttendanceHandler.cs`, `DailyAttendanceSummaryHandler.cs`, `StudentAttendanceHandler.cs`,
`TeacherAttendanceHandler.cs`, `StaffAttendanceHandler.cs`, `DashboardSummaryHandler.cs` if present,
`StudentSearchHandler.cs`, `StudentDetailsHandler.cs`, `TeacherSearchHandler.cs`, `StaffSearchHandler.cs`,
`UpcomingExamSearchHandler.cs`, `HomeworkSearchHandler.cs`, `SubjectSearchHandler.cs`,
`BusLocationSearchHandler.cs`, `GreetByIdHandler.cs`). Every one of these is a handler-level call —
apply the `"no_match"` rule to all of them.

- [ ] **Step 2: Fix the orchestrator's `TerminalAsync` helper**

In `src/Sms.Application/Services/AiSearch/AiSearchService.cs`, find the private `TerminalAsync` method
(it currently calls `AiSearchResponse.Terminal(language, outcomeIntent, answer)` with 3 args). Add a
`status` parameter derived from `outcomeIntent` and pass it through:

```csharp
    private async Task<AiSearchResponse> TerminalAsync(
        IReadOnlyList<string> callerRoles, string query, string language,
        string outcomeIntent, string auditedIntent, string answer, CancellationToken ct)
    {
        await audit.LogAsync(
            TenantId, UserId, PrimaryRole(callerRoles), query, language, auditedIntent, 0, false, ct);
        var status = outcomeIntent switch
        {
            "Forbidden" => "forbidden",
            "WriteBlocked" => "write_blocked",
            _ => "unsupported",
        };
        return AiSearchResponse.Terminal(language, outcomeIntent, answer, status);
    }
```

- [ ] **Step 3: Fix every handler-level call site**

For each file found in Step 1 (excluding `AiSearchService.cs`, already fixed), open it, find every
`AiSearchResponse.Terminal(language, "Unsupported", ...)` call (handler-level no-match/no-resolve
outcomes), and add `, "no_match"` as the fourth argument. Example, in `GreetByIdHandler.cs`:

```csharp
    private AiSearchResponse NoMatch(string language) =>
        AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");
```

Apply the identical pattern (append `"no_match"`) to every other handler's equivalent private
`NoMatch`/`Terminal(...)`-calling helper. Read each file before editing — some handlers may name
their helper differently or inline the call rather than using a private method; match whatever style
that file already uses, just add the fourth argument.

- [ ] **Step 4: Build to find anything missed**

Run: `dotnet build src/Sms.Api -c Debug --nologo`
Expected: Build succeeded, 0 errors. If any `Terminal(...)` call site remains unfixed, the compiler
reports it exactly — fix it with the same rule (handler-level → `"no_match"`).

- [ ] **Step 5: Run the full existing AiSearch test suite**

Run: `dotnet test tests/Sms.Tests.Unit --filter FullyQualifiedName~AiSearch -v n`
Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~AiSearch -v n`
Expected: Every existing test still passes unchanged (they assert on `intent`, not `status`, so
nothing about this retrofit should break an existing assertion) — if a test literally asserts
`response.Status` somewhere unexpected, that's new coupling from this task's own Task 3, not a
pre-existing test; otherwise a failure here means Step 3 missed a real behavior change, investigate
before proceeding.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/
git commit -m "fix(ai-search): retrofit every Terminal() call site with status - no_match for handler-level refusals, orchestrator outcomes for classifier-level ones"
```

---

### Task 5: `driver` role + `PersonLookup`/`MyTripStatus` access rules

**Files:**
- Modify: `src/Sms.Shared.Kernel/Authz/Policies.cs`
- Modify: `src/Sms.Application/Services/AiSearch/AiIntentAccessRules.cs`
- Test: `tests/Sms.Tests.Unit/AiSearch/AiIntentAccessRulesTests.cs`
- Test: `tests/Sms.Tests.Unit/Authz/PoliciesTests.cs` (new, if no existing test file covers `Policies.All`)

**Interfaces:**
- Produces: `Policies.Driver = "driver"`, added to `Policies.All`. `AiIntentAccessRules` gains
  `"PersonLookup"` (all six existing roles) and `"MyTripStatus"` (driver only).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Sms.Tests.Unit/AiSearch/AiIntentAccessRulesTests.cs` (following its existing `[Theory]`
pattern — read the file first for the exact style):

```csharp
    [Theory]
    [InlineData("school.admin")]
    [InlineData("school.owner")]
    [InlineData("school.principal")]
    [InlineData("school.teacher")]
    [InlineData("staff")]
    [InlineData("student.parent")]
    public void PersonLookup_is_allowed_for_every_existing_role(string role)
    {
        AiIntentAccessRules.IsAllowed("PersonLookup", [role]).Should().BeTrue();
    }

    [Fact]
    public void MyTripStatus_is_allowed_only_for_driver()
    {
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["driver"]).Should().BeTrue();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["school.admin"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["school.teacher"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["staff"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["student.parent"]).Should().BeFalse();
    }

    [Theory]
    [InlineData("DailyAttendanceSummary")]
    [InlineData("ClassAttendance")]
    [InlineData("StudentAttendance")]
    [InlineData("TeacherAttendance")]
    [InlineData("StaffAttendance")]
    [InlineData("DashboardSummary")]
    [InlineData("StudentSearch")]
    [InlineData("StudentDetails")]
    [InlineData("TeacherSearch")]
    [InlineData("StaffSearch")]
    [InlineData("UpcomingExamSearch")]
    [InlineData("HomeworkSearch")]
    [InlineData("SubjectSearch")]
    [InlineData("BusLocationSearch")]
    [InlineData("GreetById")]
    [InlineData("PersonLookup")]
    public void Driver_is_denied_every_intent_except_MyTripStatus(string intent)
    {
        AiIntentAccessRules.IsAllowed(intent, ["driver"]).Should().BeFalse(
            "driver's AI surface is deliberately the smallest of any role - promoting it into Policies.All must not widen any existing intent");
    }
```

Create `tests/Sms.Tests.Unit/Authz/PoliciesTests.cs` if no equivalent test file exists (check for one
first: `grep -rln "Policies.All" tests/`):

```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class PoliciesTests
{
    [Fact]
    public void Driver_is_a_canonical_policy_alongside_the_existing_six()
    {
        Policies.All.Should().Contain(Policies.Driver);
        Policies.All.Should().HaveCount(8, "the original 7 plus the new driver policy");
    }
}
```

Read `Policies.cs` before writing this test to confirm the current count (7 as of this plan's
writing — `PlatformOnly, SchoolAdmin, SchoolOwner, Principal, Teacher, Staff, StudentOrParent`); adjust
the expected count if it has changed.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiIntentAccessRulesTests -v n`
Run: `dotnet test tests/Sms.Tests.Unit --filter PoliciesTests -v n`
Expected: FAIL — `Policies.Driver` and the two new intent rows don't exist yet.

- [ ] **Step 3: Add `Policies.Driver`**

In `src/Sms.Shared.Kernel/Authz/Policies.cs`, add the constant and include it in `All`:

```csharp
    public const string Driver = "driver";
```

Add `Driver` to the `All` array (keep the existing six, append this one at the end).

- [ ] **Step 4: Add the two new `AiIntentAccessRules` rows**

In `src/Sms.Application/Services/AiSearch/AiIntentAccessRules.cs`, add a `Driver` constant alongside
the existing role constants, and two new dictionary entries:

```csharp
    private const string Driver = "driver";
```
```csharp
        ["PersonLookup"] = [Admin, Owner, Principal, Teacher, Staff, Parent],
        ["MyTripStatus"] = [Driver],
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiIntentAccessRulesTests -v n`
Run: `dotnet test tests/Sms.Tests.Unit --filter PoliciesTests -v n`
Expected: PASS (all cases, including the driver-denied-everywhere-else regression guard)

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Authz/Policies.cs src/Sms.Application/Services/AiSearch/AiIntentAccessRules.cs tests/Sms.Tests.Unit/AiSearch/AiIntentAccessRulesTests.cs tests/Sms.Tests.Unit/Authz/PoliciesTests.cs
git commit -m "feat(ai-search): promote driver into Policies.All, add PersonLookup and MyTripStatus access rules"
```

---

### Task 6: `AiSearchAuthorizationService` driver branch

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/AiSearchAuthorizationService.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/AiSearchAuthorizationServiceTests.cs`

**Interfaces:**
- Consumes: `Policies.Driver` (Task 5).
- Produces: no new public members — `AuthorizeAsync` now recognizes the `driver` role explicitly.

**Why this needs its own task, not just a MyTripStatusHandler:** without an explicit branch,
`AiSearchAuthorizationService`'s existing code falls through to its final `// Admin/principal/owner/staff`
branch for ANY role it doesn't recognize as parent/teacher — which would mark a driver
`Unrestricted = true`. `MyTripStatusHandler` (Task 10) never reads `Unrestricted` and is self-scoped
regardless, so this isn't an exploitable leak today, but it mislabels a driver's authorization result
in a way that would bite the first time future code branches on `Unrestricted` assuming it always
means "whole tenant." Fix the mislabeling at the source.

- [ ] **Step 1: Write the failing test**

Add to `tests/Sms.Tests.Integration/AiSearch/AiSearchAuthorizationServiceTests.cs` (read the file
first for its exact fixture/DI-construction pattern):

```csharp
    [Fact]
    public async Task Driver_role_is_not_Unrestricted_it_is_self_scoped_like_TargetSelf()
    {
        var auth = /* construct AiSearchAuthorizationService per this file's existing pattern */;

        var result = await auth.AuthorizeAsync(
            "MyTripStatus", new AiSearchFilters(null, null, null, null, false), ["driver"]);

        result.Allowed.Should().BeTrue();
        result.Unrestricted.Should().BeFalse(
            "a driver's own-trip lookup is self-scoped, never whole-tenant, even though no per-record clamp list applies");
        result.AllowedChildStudentIds.Should().BeNull();
        result.AllowedClassNames.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter Driver_role_is_not_Unrestricted -v n`
Expected: FAIL — `result.Unrestricted` is currently `true` (falls through to the default branch).

- [ ] **Step 3: Add the driver branch**

In `src/Sms.Application/Services/AiSearch/AiSearchAuthorizationService.cs`, add a `driver` role check
and an explicit branch, placed BEFORE the final `// Admin/principal/owner/staff` fallthrough comment
and its `return Allowed(intent, null, null, null, filters, unrestricted: true);` line:

```csharp
        var isDriver = callerRoles.Any(r => string.Equals(r, "driver", StringComparison.OrdinalIgnoreCase));
```

(add this alongside the existing `isParent`/`isTeacher`/`isAdminLike` declarations near the top of
`AuthorizeAsync`), and add the branch itself right before the final fallthrough:

```csharp
        // Driver: self-scoped like TargetSelf/TeacherAttendance/StaffAttendance -- MyTripStatusHandler
        // resolves the caller's own current trip via ITenantContext internally, never a request-supplied
        // id. Explicitly NOT Unrestricted -- a driver's AI surface is the smallest of any role and must
        // never be mistaken for whole-tenant scope by future code that branches on Unrestricted.
        if (isDriver)
            return Allowed(intent, null, null, null, filters);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter Driver_role_is_not_Unrestricted -v n`
Expected: PASS

- [ ] **Step 5: Run the full existing authorization-service suite to confirm no regression**

Run: `dotnet test tests/Sms.Tests.Integration --filter AiSearchAuthorizationServiceTests -v n`
Expected: PASS, all cases (the new branch is additive and only triggers for `isDriver`, which was
previously never checked).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/AiSearchAuthorizationService.cs tests/Sms.Tests.Integration/AiSearch/AiSearchAuthorizationServiceTests.cs
git commit -m "fix(ai-search): give driver its own explicit, non-Unrestricted authorization branch"
```

---

### Task 7: `IPersonResolver` / `PersonResolver`

**Files:**
- Create: `src/Sms.Application/Services/AiSearch/PersonResolver.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/PersonResolverTests.cs`

**Interfaces:**
- Consumes: `ISisService.ListMyChildrenAsync`/`ListStudentsAsync` (existing), `TeacherRepository.ListAsync`/`StaffRepository.ListAsync` (existing),
  `IUserDirectoryLookup.SearchByNameAsync` (Task 2), `ClassRepository.ListForTeacherAsync` (existing),
  `StudentClassScope.ClassMatches` (existing), `AiAuthorizationResult` (existing).
- Produces: `PersonMatch(Guid Id, string Name, string Type, string? Detail)`,
  `IPersonResolver.ResolveAsync(string name, AiAuthorizationResult auth, CancellationToken ct) : Task<IReadOnlyList<PersonMatch>>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sms.Tests.Integration/AiSearch/PersonResolverTests.cs`, following the exact
`WebApplicationFactory`/seeding conventions already established in `GreetByIdHandlerTests.cs` (read
that file first — it seeds students/teachers/classes and constructs the real
`AiSearchAuthorizationService` the same way this test needs to):

```csharp
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class PersonResolverTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Parent_search_only_ever_reaches_their_own_linked_children_never_teachers_or_staff()
    {
        // Seed: parent linked to child "Rahul Verma" (Class 8A). Seed an unrelated real teacher also
        // named "Rahul Sharma" and an unrelated real staff member "Rahul Khan" in the SAME tenant.
        // Act: resolver.ResolveAsync("Rahul", <parent's authorized AiAuthorizationResult>)
        // Assert: exactly one match, Type == "student", Name == "Rahul Verma" -- the teacher and staff
        // members named Rahul must never appear in the result.
    }

    [Fact]
    public async Task Teacher_search_is_scoped_to_students_in_their_own_classes_via_GradeSection_membership()
    {
        // Seed: teacher assigned to a class with Grade="8", Section="A" (Classes.Name deliberately
        // NOT a compacted label, e.g. "Section Eight A" -- mirrors the fix already proven in
        // GreetByIdHandlerTests). Seed a student "Rahul Verma" with ClassLabel="8-A" (matches via
        // Grade+Section) and a second real student "Rahul Khan" in a DIFFERENT class the teacher does
        // not teach.
        // Act: resolver.ResolveAsync("Rahul", <teacher's authorized AiAuthorizationResult>)
        // Assert: exactly one match (Rahul Verma) -- Rahul Khan (real, same first name, wrong class)
        // must never appear.
    }

    [Fact]
    public async Task Unrestricted_search_fans_out_across_all_four_sources_and_finds_all_matches()
    {
        // Seed, all in one tenant: a student "Rahul Verma", a teacher "Rahul Sharma", a staff member
        // "Rahul Khan", and an admin account (Users row + UserRoles 'school.admin') named "Rahul Gupta".
        // Act: resolver.ResolveAsync("Rahul", <admin's Unrestricted AiAuthorizationResult>)
        // Assert: exactly 4 matches, one of each Type ("student", "teacher", "staff", "admin"), each
        // with the expected Name.
    }

    [Fact]
    public async Task Cross_tenant_same_name_students_never_appear_together()
    {
        // Seed a student "Rahul Verma" in tenant A and a DIFFERENT student also named "Rahul Verma" in
        // tenant B.
        // Act: resolver.ResolveAsync("Rahul", <tenant A admin's Unrestricted AiAuthorizationResult>)
        // Assert: exactly 1 match (tenant A's), by Id -- never 2.
    }

    [Fact]
    public async Task Two_admins_with_the_same_name_and_role_get_a_masked_email_tie_breaker_in_Detail()
    {
        // Seed two Users rows in the same tenant, both Name="Rahul Sharma", both role 'school.admin',
        // with distinct emails (e.g. "rahul.s@school.test" and "rahul.sharma2@school.test").
        // Act: resolver.ResolveAsync("Rahul", <admin's Unrestricted AiAuthorizationResult>)
        // Assert: 2 matches, both Type=="admin", each Detail contains a masked form of its own email
        // (first character + asterisks + "@domain") -- assert the RAW email never appears unmasked
        // anywhere in either Detail string.
    }

    [Fact]
    public async Task A_single_admin_with_no_name_collision_gets_the_plain_role_label_as_Detail()
    {
        // Seed one admin "Rahul Gupta", no other same-named admin/owner/principal in the tenant.
        // Act + Assert: Detail == "Admin" (or whatever exact label string Task 7's implementation
        // uses -- pin the actual string here once written, don't guess it in this brief).
    }

    [Fact]
    public async Task IsStillInTeacherScopeAsync_is_true_for_a_student_in_a_class_the_teacher_teaches()
    {
        // Seed a teacher assigned to Grade="8"/Section="A" (Classes.Name deliberately NOT a compacted
        // label). Seed a student with ClassLabel="8-A" in the same tenant.
        // Act: resolver.IsStillInTeacherScopeAsync(student.Id, <the teacher's real AllowedClassNames
        // from a real AuthorizeAsync call>)
        // Assert: true.
    }

    [Fact]
    public async Task IsStillInTeacherScopeAsync_is_false_once_the_student_has_moved_to_a_different_class()
    {
        // Same seeding as above, but the student's Grade/Section/ClassLabel is updated (direct SQL,
        // or the real update-student endpoint) to a DIFFERENT class the teacher does not teach BEFORE
        // calling IsStillInTeacherScopeAsync.
        // Assert: false -- this is the exact regression guard Task 12's conversation-security test
        // depends on.
    }
}
```

Fill in each test body using the real seeding helpers (`Seed`, direct SQL inserts for
`Students`/`Teachers`/`Staff`/`Users`+`UserRoles`, and constructing a real
`AiSearchAuthorizationService` to get a genuine `AiAuthorizationResult` for each role) exactly as
`GreetByIdHandlerTests.cs` and `AiSearchAuthorizationServiceTests.cs` already do — do not hand-build a
fake `AiAuthorizationResult`; resolving it through the real service is what makes these tests
actually exercise the authorization boundary, not just the resolver's own filtering.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonResolverTests -v n`
Expected: FAIL — `PersonResolver`/`IPersonResolver` don't exist yet.

- [ ] **Step 3: Implement `PersonResolver`**

Create `src/Sms.Application/Services/AiSearch/PersonResolver.cs`:

```csharp
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Data;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch;

public sealed record PersonMatch(Guid Id, string Name, string Type, string? Detail);

public interface IPersonResolver
{
    Task<IReadOnlyList<PersonMatch>> ResolveAsync(
        string name, AiAuthorizationResult auth, CancellationToken ct = default);

    /// Re-authorization primitive for conversation follow-ups (Task 12): is this ALREADY-resolved
    /// student still inside a teacher's CURRENT class assignments? Reuses the exact same
    /// Grade+Section-via-ClassRepository membership check ResolveForTeacherAsync applies to a fresh
    /// name search, so a follow-up can never be less strict than an original lookup would have been.
    Task<bool> IsStillInTeacherScopeAsync(
        Guid studentId, IReadOnlyList<string> allowedClassNames, CancellationToken ct = default);
}

/// <summary>
/// Fans out across the four person-data sources this codebase has, each query independently scoped
/// by the ALREADY-authorized <see cref="AiAuthorizationResult"/> -- never a fresh, unscoped search.
/// See AiSearchAuthorizationService's doc comments for the Unrestricted/null/empty-list invariant
/// every branch below must honor exactly like every other AiSearch handler already does.
/// </summary>
public sealed class PersonResolver(
    ISisService sis, TeacherRepository teachers, StaffRepository staff,
    IUserDirectoryLookup users, ClassRepository classes, ITenantContext tenant) : IPersonResolver
{
    public async Task<IReadOnlyList<PersonMatch>> ResolveAsync(
        string name, AiAuthorizationResult auth, IReadOnlyList<string> callerRoles, CancellationToken ct = default)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return [];

        if (!auth.Unrestricted)
        {
            if (auth.AllowedChildStudentIds is not null)
                return await ResolveForParentAsync(auth.AllowedChildStudentIds, trimmed, ct);
            if (auth.AllowedClassNames is not null)
                return await ResolveForTeacherAsync(auth.AllowedClassNames, trimmed, ct);
            return [];
        }

        return await ResolveUnrestrictedAsync(trimmed, ct);
    }

    public async Task<bool> IsStillInTeacherScopeAsync(
        Guid studentId, IReadOnlyList<string> allowedClassNames, CancellationToken ct = default)
    {
        var student = await sis.GetStudentAsync(studentId, ct);
        if (!student.IsSuccess) return false;

        var teacherClasses = tenant.UserId is { } teacherUserId
            ? await classes.ListForTeacherAsync(teacherUserId, ct)
            : [];
        var authorizedClasses = teacherClasses
            .Where(c => allowedClassNames.Any(cn => string.Equals(c.Name?.Trim(), cn?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return authorizedClasses.Any(c =>
            StudentClassScope.ClassMatches(c, student.Data!.Grade, student.Data!.Section, student.Data!.ClassLabel));
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveForParentAsync(
        IReadOnlyList<Guid> allowedChildIds, string name, CancellationToken ct)
    {
        var children = await sis.ListMyChildrenAsync(ct);
        if (!children.IsSuccess) return [];

        return children.Data!
            .Where(c => allowedChildIds.Contains(c.Id) && c.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(c => new PersonMatch(c.Id, c.Name, "student", c.ClassLabel))
            .ToList();
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveForTeacherAsync(
        IReadOnlyList<string> allowedClassNames, string name, CancellationToken ct)
    {
        var result = await sis.ListStudentsAsync(name, null, null, null, ct: ct);
        if (!result.IsSuccess) return [];

        var teacherClasses = tenant.UserId is { } teacherUserId
            ? await classes.ListForTeacherAsync(teacherUserId, ct)
            : [];
        var authorizedClasses = teacherClasses
            .Where(c => allowedClassNames.Any(cn => string.Equals(c.Name?.Trim(), cn?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return result.Data!.Data
            .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                && authorizedClasses.Any(c => StudentClassScope.ClassMatches(c, s.Grade, s.Section, s.ClassLabel)))
            .Select(s => new PersonMatch(s.Id, s.Name, "student", s.ClassLabel))
            .ToList();
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveUnrestrictedAsync(string name, CancellationToken ct)
    {
        var matches = new List<PersonMatch>();

        var students = await sis.ListStudentsAsync(name, null, null, null, ct: ct);
        if (students.IsSuccess)
            matches.AddRange(students.Data!.Data
                .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(s => new PersonMatch(s.Id, s.Name, "student", s.ClassLabel)));

        var teacherRows = await teachers.ListAsync(name, null, null, ct);
        matches.AddRange(teacherRows
            .Where(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(t => new PersonMatch(t.Id, t.Name, "teacher", t.Department)));

        var staffRows = await staff.ListAsync(name, null, ct);
        matches.AddRange(staffRows
            .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(s => new PersonMatch(s.Id, s.Name, "staff", s.Department)));

        var userMatches = await users.SearchByNameAsync(name, ct);
        matches.AddRange(ResolveAdminDetails(userMatches));

        return matches;
    }

    /// Two matches sharing both Name AND Type (rare -- e.g. two Owners named "Rahul Sharma") get a
    /// masked-email tie-breaker in Detail instead of the plain role label. A single, unambiguous
    /// match for its name+type just gets the role label.
    private static IEnumerable<PersonMatch> ResolveAdminDetails(IReadOnlyList<UserDirectoryMatch> raw)
    {
        var groups = raw.GroupBy(r => (Name: r.Name.Trim().ToLowerInvariant(), r.Type));
        foreach (var group in groups)
        {
            var list = group.ToList();
            var ambiguous = list.Count > 1;
            foreach (var r in list)
            {
                var detail = ambiguous ? $"{RoleLabel(r.Type)} ({MaskEmail(r.Email)})" : RoleLabel(r.Type);
                yield return new PersonMatch(r.Id, r.Name, r.Type, detail);
            }
        }
    }

    private static string RoleLabel(string type) => type switch
    {
        "owner" => "Owner",
        "principal" => "Principal",
        _ => "Admin",
    };

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}{new string('*', at - 1)}{email[at..]}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonResolverTests -v n`
Expected: PASS (all 8 cases)

- [ ] **Step 5: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`:

```csharp
builder.Services.AddScoped<IPersonResolver, PersonResolver>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/PersonResolver.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/AiSearch/PersonResolverTests.cs
git commit -m "feat(ai-search): add PersonResolver - four-source name fan-out scoped by AiAuthorizationResult"
```

---

### Task 8: Person-type answer templates

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/AiAnswerTemplateService.cs`
- Test: `tests/Sms.Tests.Unit/AiSearch/AiAnswerTemplateServiceTests.cs`

**Interfaces:**
- Produces: `IAiAnswerTemplateService.RenderPersonIsStudent(string language, string name, string? classLabel) : string`,
  `RenderPersonIsTeacher(string language, string name, IReadOnlyList<string> subjects) : string`,
  `RenderPersonIsStaffLike(string language, string name, string roleLabel) : string`,
  `RenderNoActiveTrip(string language) : string`,
  `RenderTripStatus(string language, string busNo, string direction, string status) : string`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Sms.Tests.Unit/AiSearch/AiAnswerTemplateServiceTests.cs` (read the file first for its
exact style — likely one `[Theory]` per method with en/hi/hinglish cases, matching the pattern
already used for `RenderGreeting`):

```csharp
    [Theory]
    [InlineData("en", "Rahul Sharma is a Teacher.")]
    [InlineData("hi", "Rahul Sharma ek Teacher hain.")]
    [InlineData("hinglish", "Rahul Sharma ek Teacher hain.")]
    public void RenderPersonIsTeacher_names_the_role_first_then_subjects(string language, string expectedStart)
    {
        var service = new AiAnswerTemplateService();
        var answer = service.RenderPersonIsTeacher(language, "Rahul Sharma", ["Mathematics"]);
        answer.Should().StartWith(expectedStart.Split(" hain")[0]); // loose start-of-sentence check; exact strings pinned in Step 3 below
        answer.Should().Contain("Mathematics");
    }

    [Fact]
    public void RenderPersonIsTeacher_lists_multiple_subjects()
    {
        var service = new AiAnswerTemplateService();
        var answer = service.RenderPersonIsTeacher("en", "Rahul Sharma", ["Mathematics", "Physics"]);
        answer.Should().Contain("Mathematics").And.Contain("Physics");
    }

    [Fact]
    public void RenderPersonIsStudent_includes_the_class_label_when_present()
    {
        var service = new AiAnswerTemplateService();
        var answer = service.RenderPersonIsStudent("en", "Rahul Verma", "8-A");
        answer.Should().Contain("Rahul Verma").And.Contain("8-A").And.Contain("Student");
    }

    [Fact]
    public void RenderPersonIsStaffLike_uses_the_supplied_role_label_verbatim()
    {
        var service = new AiAnswerTemplateService();
        service.RenderPersonIsStaffLike("en", "Rahul Khan", "Owner").Should().Contain("Owner");
    }

    [Fact]
    public void RenderNoActiveTrip_and_RenderTripStatus_are_distinct_per_language()
    {
        var service = new AiAnswerTemplateService();
        service.RenderNoActiveTrip("en").Should().NotBe(service.RenderNoActiveTrip("hi"));
        service.RenderTripStatus("en", "BUS-12", "morning", "in_progress")
            .Should().Contain("BUS-12");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiAnswerTemplateServiceTests -v n`
Expected: FAIL — the five new methods don't exist.

- [ ] **Step 3: Add the methods**

In `src/Sms.Application/Services/AiSearch/AiAnswerTemplateService.cs`, add to the interface:

```csharp
    string RenderPersonIsStudent(string language, string name, string? classLabel);
    string RenderPersonIsTeacher(string language, string name, IReadOnlyList<string> subjects);
    string RenderPersonIsStaffLike(string language, string name, string roleLabel);
    string RenderNoActiveTrip(string language);
    string RenderTripStatus(string language, string busNo, string direction, string status);
```

And to the implementation:

```csharp
    public string RenderPersonIsStudent(string language, string name, string? classLabel)
    {
        var cls = classLabel ?? "";
        return language switch
        {
            "hi" => $"{name} एक Student हैं, कक्षा {cls}.",
            "hinglish" => $"{name} ek Student hain, class {cls}.",
            _ => $"{name} is a Student in class {cls}.",
        };
    }

    public string RenderPersonIsTeacher(string language, string name, IReadOnlyList<string> subjects)
    {
        var list = subjects.Count == 0 ? "" : string.Join(", ", subjects);
        return language switch
        {
            "hi" => $"{name} ek Teacher hain. Ye {list} padhate hain.",
            "hinglish" => $"{name} ek Teacher hain. Ye {list} padhate hain.",
            _ => $"{name} is a Teacher. {(subjects.Count > 1 ? "He/She teaches" : "He/She teaches")} {list}.",
        };
    }

    public string RenderPersonIsStaffLike(string language, string name, string roleLabel) =>
        language switch
        {
            "hi" => $"{name} ek {roleLabel} hain.",
            "hinglish" => $"{name} ek {roleLabel} hain.",
            _ => $"{name} is a {roleLabel}.",
        };

    public string RenderNoActiveTrip(string language) =>
        language switch
        {
            "hi" => "अभी कोई सक्रिय ट्रिप नहीं है।",
            "hinglish" => "Abhi koi active trip nahi hai.",
            _ => "You have no active trip right now.",
        };

    public string RenderTripStatus(string language, string busNo, string direction, string status) =>
        language switch
        {
            "hi" => $"आपकी बस {busNo} {direction} की ओर, स्थिति: {status}.",
            "hinglish" => $"Aapki bus {busNo} {direction} ki taraf, status: {status}.",
            _ => $"Your bus {busNo} is heading {direction}, status: {status}.",
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiAnswerTemplateServiceTests -v n`
Expected: PASS. If the loose `StartWith` assertion in the first theory doesn't match your exact
template strings, fix the TEST to assert the real string your implementation produces (pin it
exactly) — don't loosen the implementation to fit a guessed string.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/AiAnswerTemplateService.cs tests/Sms.Tests.Unit/AiSearch/AiAnswerTemplateServiceTests.cs
git commit -m "feat(ai-search): add person-type and trip-status answer templates (en/hi/hinglish)"
```

---

### Task 9: `PersonLookupHandler`

**Files:**
- Create: `src/Sms.Application/Services/AiSearch/Handlers/PersonLookupHandler.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/PersonLookupHandlerTests.cs`

**Interfaces:**
- Consumes: `IPersonResolver` (Task 7), `IAiAnswerTemplateService`'s new methods (Task 8),
  `TeacherRepository` (existing, for subject lookup), `AiSearchResponse.NeedsClarification` (Task 3).
- Produces: `PersonLookupHandler : IAiIntentHandler`, `Intent => "PersonLookup"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sms.Tests.Integration/AiSearch/PersonLookupHandlerTests.cs`, following
`GreetByIdHandlerTests.cs`'s exact `Handle`/`AuthorizeAndHandle` helper pattern (read that file's
helpers first and reuse them rather than reinventing):

```csharp
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class PersonLookupHandlerTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Single_teacher_match_renders_role_and_subjects_and_sets_ConversationUpdate()
    {
        // Seed a teacher "Rahul Sharma" teaching Mathematics, no other Rahul in the tenant.
        // Act: handler.HandleAsync(<admin's Unrestricted auth, StudentName="Rahul">, "en", 1, 20)
        // Assert: response.Status == "success", response.Answer contains "Teacher" and "Mathematics",
        // response.ConversationUpdate is not null, ResolvedEntityType == "teacher",
        // ResolvedEntityId == the teacher's real id.
    }

    [Fact]
    public async Task Single_student_match_renders_student_shape()
    {
        // Seed a student "Rahul Verma", Class 8A, no other Rahul.
        // Assert: response.Answer contains "Student" and "8-A" (or the real ClassLabel format).
    }

    [Fact]
    public async Task Zero_matches_returns_no_match_status_with_Unsupported_intent()
    {
        // No Rahul seeded at all.
        // Assert: response.Status == "no_match", response.Intent == "Unsupported" (non-breaking),
        // response.Data is null, response.ConversationUpdate is null.
    }

    [Fact]
    public async Task Multiple_matches_returns_needs_clarification_with_safe_fields_only_and_sets_PendingCandidates()
    {
        // Seed a teacher "Rahul Sharma" (Mathematics) and a student "Rahul Verma" (Class 8A) in the
        // same tenant, admin caller.
        // Assert: response.Status == "needs_clarification", response.Data is a list of exactly 2
        // items each shaped {name, type, detail} (assert via raw JSON that no GUID-shaped id string
        // appears anywhere in response.Data's serialized form), response.ConversationUpdate is not
        // null, PendingCandidates has exactly 2 entries with the REAL ids and types (this field is
        // never serialized to the client -- assert it directly on the C# object, not via JSON).
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonLookupHandlerTests -v n`
Expected: FAIL — `PersonLookupHandler` does not exist.

- [ ] **Step 3: Implement the handler**

Create `src/Sms.Application/Services/AiSearch/Handlers/PersonLookupHandler.cs`:

```csharp
using Sms.Modules.Staffing.Data;

namespace Sms.Application.Services.AiSearch.Handlers;

public sealed class PersonLookupHandler(
    IPersonResolver resolver, IAiAnswerTemplateService templates, TeacherRepository teachers)
    : IAiIntentHandler
{
    public const string IntentName = "PersonLookup";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var name = auth.ClampedFilters.StudentName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var matches = await resolver.ResolveAsync(name, auth, ct);

        if (matches.Count == 0)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        if (matches.Count > 1)
        {
            var candidates = matches.Select(m => new PersonCandidate(m.Name, m.Type, m.Detail)).ToList();
            var pending = matches.Select(m => new PendingCandidate(m.Id, m.Type)).ToList();
            var clarifyAnswer = templates.RenderNeedsClarification(language, candidates.Count);
            return AiSearchResponse.NeedsClarification(language, IntentName, clarifyAnswer, candidates)
                with { ConversationUpdate = new AiConversationUpdate(null, null, pending) };
        }

        var match = matches[0];
        var answer = await RenderAsync(match, language, ct);
        var data = new { id = match.Id, name = match.Name, type = match.Type, detail = match.Detail };
        return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(match.Id, match.Type, null) };
    }

    private async Task<string> RenderAsync(PersonMatch match, string language, CancellationToken ct)
    {
        if (match.Type == "teacher")
        {
            var rows = await teachers.ListAsync(match.Name, null, null, ct);
            var subjects = rows.FirstOrDefault(t => t.Id == match.Id)?.Subjects ?? [];
            return templates.RenderPersonIsTeacher(language, match.Name, subjects);
        }
        if (match.Type == "student")
            return templates.RenderPersonIsStudent(language, match.Name, match.Detail);

        // staff / admin / owner / principal all share the same staff-like shape -- Detail already
        // carries the exact role label to show (see PersonResolver.ResolveAdminDetails/RoleLabel).
        return templates.RenderPersonIsStaffLike(language, match.Name, match.Detail ?? match.Type);
    }
}
```

This introduces one more template method not yet added in Task 8 — add it there too (go back and add
to `IAiAnswerTemplateService`/`AiAnswerTemplateService`):

```csharp
    string RenderNeedsClarification(string language, int count);
```
```csharp
    public string RenderNeedsClarification(string language, int count) =>
        language switch
        {
            "hi" => $"मुझे इस नाम के {count} लोग मिले। आप किसकी बात कर रहे हैं?",
            "hinglish" => $"Mujhe is naam ke {count} log mile. Aap kiski baat kar rahe hain?",
            _ => $"I found {count} people with that name. Which one do you mean?",
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonLookupHandlerTests -v n`
Expected: PASS (all 4 cases)

- [ ] **Step 5: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, alongside the other
`AddScoped<IAiIntentHandler, ...>()` registrations:

```csharp
builder.Services.AddScoped<IAiIntentHandler, PersonLookupHandler>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/Handlers/PersonLookupHandler.cs src/Sms.Application/Services/AiSearch/AiAnswerTemplateService.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/AiSearch/PersonLookupHandlerTests.cs
git commit -m "feat(ai-search): add PersonLookupHandler with single/no-match/needs-clarification outcomes"
```

---

### Task 10: `MyTripStatusHandler` (driver)

**Files:**
- Create: `src/Sms.Application/Services/AiSearch/Handlers/MyTripStatusHandler.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/MyTripStatusHandlerTests.cs`

**Interfaces:**
- Consumes: `ITripService.GetCurrentAsync()` (existing, already self-scoped via `ITenantContext` —
  takes no parameters), `IAiAnswerTemplateService.RenderTripStatus`/`RenderNoActiveTrip` (Task 8).
- Produces: `MyTripStatusHandler : IAiIntentHandler`, `Intent => "MyTripStatus"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sms.Tests.Integration/AiSearch/MyTripStatusHandlerTests.cs`, following whatever seeding
pattern `tests/Sms.Tests.Integration/Transport/` already uses for starting a trip for a driver (check
`StaffTripAssignmentTests.cs`'s existing helpers first — likely already known to the codebase, do not
invent new trip-seeding SQL):

```csharp
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class MyTripStatusHandlerTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Driver_with_an_active_trip_gets_bus_and_status()
    {
        // Seed a driver with a started trip (BusNo="BUS-12", Direction="morning", Status="in_progress").
        // Act: handler.HandleAsync(<driver's authorized auth>, "en", 1, 20)
        // Assert: response.Status == "success", response.Answer contains "BUS-12".
    }

    [Fact]
    public async Task Driver_with_no_active_trip_gets_a_clean_no_active_trip_answer()
    {
        // No trip started for this driver.
        // Assert: response.Status == "no_match", response.Answer equals RenderNoActiveTrip("en")'s exact string.
    }

    [Fact]
    public async Task A_driver_never_sees_another_drivers_trip()
    {
        // Seed driver A with an active trip and driver B with none. Act as driver B.
        // Assert: driver B still gets "no active trip", never driver A's trip data -- this is really
        // asserting ITripService.GetCurrentAsync()'s own existing self-scoping holds, as a regression
        // guard specific to how this handler consumes it.
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter MyTripStatusHandlerTests -v n`
Expected: FAIL — `MyTripStatusHandler` does not exist.

- [ ] **Step 3: Implement the handler**

Create `src/Sms.Application/Services/AiSearch/Handlers/MyTripStatusHandler.cs`:

```csharp
using Sms.Application.Services.Transport;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Self-scoped exactly like TeacherAttendance/StaffAttendance -- ITripService.GetCurrentAsync()
/// already resolves the caller's own trip internally via ITenantContext, never a request-supplied
/// id, so this handler never reads any field off `auth` beyond confirming it was Allowed (checked
/// upstream by AiSearchService before any handler runs).
/// </summary>
public sealed class MyTripStatusHandler(ITripService trips, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public const string IntentName = "MyTripStatus";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await trips.GetCurrentAsync(ct);
        if (!result.IsSuccess || result.Data is not { } trip)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoActiveTrip(language), "no_match");

        var answer = templates.RenderTripStatus(language, trip.BusNo ?? "", trip.Direction, trip.Status);
        var data = new { trip.Id, trip.BusNo, trip.Direction, trip.Status, trip.StartedAt };
        return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false);
    }
}
```

Check `ITripService`'s actual return type for `GetCurrentAsync` (`ApiResult<TripResponse?>` as of this
plan's writing — confirm `TripResponse`'s exact field names in
`src/Sms.Modules.Transport/TransportModule.cs` before finalizing this file, in case they've shifted).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter MyTripStatusHandlerTests -v n`
Expected: PASS (all 3 cases)

- [ ] **Step 5: Register in DI**

```csharp
builder.Services.AddScoped<IAiIntentHandler, MyTripStatusHandler>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/Handlers/MyTripStatusHandler.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/AiSearch/MyTripStatusHandlerTests.cs
git commit -m "feat(ai-search): add MyTripStatusHandler, self-scoped for the new driver role"
```

---

### Task 11: Classifier — `PersonLookup`/`MyTripStatus` intents, `languageDirective`, context-hint injection

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/IAiClassificationClient.cs`
- Modify: `src/Sms.Application/Services/AiSearch/AiClassificationClient.cs`
- Test: `tests/Sms.Tests.Unit/AiSearch/AiClassificationClientTests.cs`

**Interfaces:**
- Produces: `AiConversationHint(string EntityName, string EntityType)`,
  `IAiClassificationClient.ClassifyAsync(string query, AiConversationHint? hint, CancellationToken ct)`
  (new optional second parameter — existing single-arg call sites in tests keep compiling via the
  default), `AiClassificationResult.LanguageDirective` (Task 3 already added this field).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Sms.Tests.Unit/AiSearch/AiClassificationClientTests.cs` (read the file first — it
already has a `FakeHandler`/`MakeClient` pattern from `GreetById`'s own classifier test, reuse it):

```csharp
    [Fact]
    public async Task ClassifyAsync_parses_languageDirective_when_present()
    {
        var client = MakeClient("""
            {"language":"hinglish","intent":"DailyAttendanceSummary",
             "filters":{"studentName":null,"className":null,"section":null,"dateExpression":"aaj","targetSelf":false},
             "languageDirective":"hi"}
            """);

        var result = await client.ClassifyAsync("Hindi mein batao, aaj kitne bachche aaye?");

        result.LanguageDirective.Should().Be("hi");
    }

    [Fact]
    public async Task ClassifyAsync_defaults_languageDirective_to_null_when_absent()
    {
        var client = MakeClient("""
            {"language":"en","intent":"StudentSearch",
             "filters":{"studentName":"Rahul","className":null,"section":null,"dateExpression":null,"targetSelf":false}}
            """);

        var result = await client.ClassifyAsync("who is Rahul");

        result.LanguageDirective.Should().BeNull();
    }

    [Fact]
    public async Task ClassifyAsync_with_a_hint_sends_the_prior_entity_in_the_system_prompt()
    {
        RecordedSystemPrompt = null;
        var client = MakeClientCapturingSystemPrompt("""
            {"language":"en","intent":"PersonLookup",
             "filters":{"studentName":null,"className":null,"section":null,"dateExpression":null,"targetSelf":false}}
            """);

        await client.ClassifyAsync("Kya padhate hain?", new AiConversationHint("Rahul Sharma", "teacher"));

        RecordedSystemPrompt.Should().Contain("Rahul Sharma").And.Contain("teacher");
    }
```

You'll need a small addition to this test file's existing fake-handler infrastructure to capture the
outgoing request body (specifically the `system` field) rather than only scripting the response —
read the existing `FakeHandler`/`MakeClient` helpers in this file and add a
`MakeClientCapturingSystemPrompt` variant (or extend the existing fake handler with an optional
capture callback) rather than duplicating the whole class; a static field `RecordedSystemPrompt` on
the test class, set inside the handler's `SendAsync` override, is a pragmatic, test-local way to do
this without over-engineering a mocking abstraction this codebase doesn't otherwise use.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiClassificationClientTests -v n`
Expected: FAIL — `ClassifyAsync` doesn't accept a hint parameter yet, `languageDirective` isn't parsed.

- [ ] **Step 3: Update the interface**

In `src/Sms.Application/Services/AiSearch/IAiClassificationClient.cs`:

```csharp
namespace Sms.Application.Services.AiSearch;

public sealed record AiConversationHint(string EntityName, string EntityType);

public interface IAiClassificationClient
{
    Task<AiClassificationResult> ClassifyAsync(string query, AiConversationHint? hint = null, CancellationToken ct = default);
}
```

- [ ] **Step 4: Update `AiClassificationClient`**

In `src/Sms.Application/Services/AiSearch/AiClassificationClient.cs`:

1. Change the `SystemPrompt` constant into a private static method that takes the optional hint and
   returns the assembled string:

```csharp
    private static string BuildSystemPrompt(AiConversationHint? hint)
    {
        var basePrompt = """
            You are the School Management System's read-only AI Search Assistant.
            You only identify which read-only search intent and filters match the user's question.
            You never generate INSERT, UPDATE, DELETE, MERGE, UPSERT, DROP, ALTER, TRUNCATE, CREATE, or EXEC.
            You never determine or override TenantId, UserId, role, or permissions — the backend handles that.
            If the question asks for a modification (e.g. "mark X present", "delete Y"), set intent to
            "WriteRequestDetected". If the question doesn't match any known intent, set intent to "Unsupported".
            Detect the language style as one of: en, hi, hinglish. Support mixed-language questions.
            If the message is an EXPLICIT instruction to switch response language (e.g. "Hindi mein batao",
            "reply in English", "speak in Hindi") -- not merely a message that happens to be in that
            language -- set languageDirective to "en" or "hi". Otherwise leave languageDirective unset.
            Known intents: DailyAttendanceSummary, ClassAttendance, SectionAttendance, StudentAttendance,
            TeacherAttendance, StaffAttendance, DashboardSummary, StudentSearch, StudentDetails, TeacherSearch,
            StaffSearch, UpcomingExamSearch, TestSearch, HomeworkSearch, SubjectSearch, BusLocationSearch,
            GreetById, PersonLookup, MyTripStatus.
            GreetById: the user has scanned or typed an EXACT admission number (student) or employee code
            (teacher/staff) and wants that person greeted by name. For this intent ONLY, put the exact
            scanned/typed code verbatim into filters.studentName — it is an ID, not a person's name, and
            must not be altered, guessed, or padded. Examples: "who is 4521", "greet student 4521", or a
            bare scanned code like "EMP-2291" with no other words.
            PersonLookup: the user is asking who someone IS, by name -- "Rahul kaun hai?", "who is Rahul?",
            "Rahul kya padhate hain?" (a natural follow-up once Rahul is known to be a teacher), "kaunsi
            class?" (a follow-up about which class(es) a resolved teacher teaches). Do NOT assume a named
            person is a student -- the backend resolves the actual type (student/teacher/staff/admin/
            owner/principal). Put the person's name into filters.studentName. A short follow-up with no
            name at all (e.g. "kya padhate hain?", "what does he teach?", "kaunsi class?") after a person
            has already been discussed this conversation should also classify as PersonLookup with
            filters.studentName left null -- the backend resolves it from the conversation's own context.
            MyTripStatus: a driver asking about their own current bus/trip/route -- "meri trip kya hai?",
            "what's my route today?". No filters needed. Only meaningful for the driver role, but you do
            not need to check roles -- the backend enforces that.
            Always call the classify_query tool with your answer — never respond in plain text.
            """;

        if (hint is null) return basePrompt;

        return basePrompt + $"""

            The user was just discussing {hint.EntityName}, a {hint.EntityType}. If this message is a
            natural follow-up about that same person (e.g. asking what they teach, which class, their
            role), classify it as PersonLookup and leave filters.studentName null — the backend will
            resolve it against {hint.EntityName} directly. This is context only; it grants no
            authorization and you must not assume anything about who may see this person's data.
            """;
    }
```

2. Add `languageDirective` to the tool schema's `filters`-sibling properties (it's a top-level
   classification field, not inside `filters` — add it as a sibling of `language`/`intent`/`filters`):

```csharp
                properties = new
                {
                    language = new { type = "string", @enum = new[] { "en", "hi", "hinglish" } },
                    intent = new { type = "string" },
                    languageDirective = new { type = "string", @enum = new[] { "en", "hi" } },
                    filters = new
                    {
                        // unchanged
                    }
                },
                required = new[] { "language", "intent", "filters" }
```

(`languageDirective` deliberately NOT in `required` — it's genuinely optional, absent on most turns.)

3. Update `ClassifyAsync`'s signature and body:

```csharp
    public async Task<AiClassificationResult> ClassifyAsync(
        string query, AiConversationHint? hint = null, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

            http.BaseAddress ??= new Uri(options.Value.BaseUrl);

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    model = options.Value.Model,
                    max_tokens = 512,
                    system = BuildSystemPrompt(hint),
                    tools = Tools,
                    tool_choice = new { type = "tool", name = "classify_query" },
                    messages = new[] { new { role = "user", content = query } }
                })
            };
            request.Headers.Add("x-api-key", options.Value.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            var toolUse = doc.RootElement.GetProperty("content")
                .EnumerateArray()
                .First(b => b.GetProperty("type").GetString() == "tool_use");
            var input = toolUse.GetProperty("input");

            var filtersEl = input.GetProperty("filters");
            var filters = new AiSearchFilters(
                filtersEl.TryGetProperty("studentName", out var sn) ? sn.GetString() : null,
                filtersEl.TryGetProperty("className", out var cn) ? cn.GetString() : null,
                filtersEl.TryGetProperty("section", out var se) ? se.GetString() : null,
                filtersEl.TryGetProperty("dateExpression", out var de) ? de.GetString() : null,
                filtersEl.TryGetProperty("targetSelf", out var ts) && ts.GetBoolean());

            return new AiClassificationResult(
                input.GetProperty("language").GetString() ?? "en",
                input.GetProperty("intent").GetString() ?? "Unsupported",
                filters,
                input.TryGetProperty("languageDirective", out var ld) ? ld.GetString() : null);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            return new AiClassificationResult("en", "Unsupported", new AiSearchFilters(null, null, null, null, false));
        }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter AiClassificationClientTests -v n`
Expected: PASS

- [ ] **Step 6: Fix any other call site**

Run: `grep -rn "\.ClassifyAsync(" src/ tests/` — every existing call site passing only `(query, ct)`
positionally now binds `ct` to the `hint` parameter incorrectly if not using named arguments. Check
each one: if it currently reads `classifier.ClassifyAsync(query, ct)`, change it to
`classifier.ClassifyAsync(query, ct: ct)` (named) so `hint` correctly defaults to `null`.

Run: `dotnet build src/Sms.Api -c Debug --nologo` to confirm 0 errors after this fix.

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/IAiClassificationClient.cs src/Sms.Application/Services/AiSearch/AiClassificationClient.cs tests/Sms.Tests.Unit/AiSearch/AiClassificationClientTests.cs
git commit -m "feat(ai-search): add PersonLookup/MyTripStatus intents, languageDirective, conversation-hint prompt injection"
```

---

### Task 12: `AiSearchService` — conversation wiring, re-authorization, language override

**Files:**
- Modify: `src/Sms.Application/Services/AiSearch/AiSearchService.cs`
- Modify: `src/Sms.Application/Services/AiSearch/AiSearchAuthorizationService.cs` (`AiAuthorizationResult` gains `PreResolvedEntityId`/`PreResolvedEntityType`)
- Modify: `src/Sms.Application/Services/AiSearch/Handlers/PersonLookupHandler.cs` (pre-resolved short-circuit, three new constructor dependencies)
- Modify: `src/Sms.Api/Controllers/AiSearchController.cs`
- Modify: `tests/Sms.Tests.Integration/AiSearch/PersonLookupHandlerTests.cs` (new pre-resolved test case)
- Test: `tests/Sms.Tests.Unit/AiSearch/AiSearchServiceTests.cs`
- Test: `tests/Sms.Tests.Integration/AiSearch/AiSearchConversationSecurityTests.cs` (new)

**Interfaces:**
- Consumes: everything from Tasks 1–11.
- Produces: `AiSearchService.SearchAsync` now loads/saves conversation context and echoes
  `conversation_id`; `AiSearchController` passes the request's `conversation_id` through and nothing
  else changes in the controller (the response's `conversation_id` is already on `AiSearchResponse`
  from Task 3, serialized automatically).

**This is the task that ties every prior task together — read it in full before starting.**

- [ ] **Step 1: Write the failing security-critical integration tests first**

Create `tests/Sms.Tests.Integration/AiSearch/AiSearchConversationSecurityTests.cs`, following
`AiSearchSecurityTests.cs`'s exact `ScriptedClassificationClient`/`App`/`Search` helper pattern (read
that file in full first):

```csharp
using System.Net;
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class AiSearchConversationSecurityTests(SqlServerFixture fx)
{
    [Fact]
    public async Task A_teachers_follow_up_fails_closed_after_the_student_changes_class()
    {
        // 1. Seed a teacher assigned to Class 8A, and a student "Rahul Verma" in 8A.
        // 2. Script the classifier to return PersonLookup/studentName="Rahul" -- act as the teacher,
        //    resolve Rahul, capture the returned conversation_id.
        // 3. Move Rahul to a DIFFERENT class the teacher does NOT teach (direct SQL update, or the
        //    real move-student endpoint if one exists).
        // 4. Script the classifier to return PersonLookup with studentName=null (a natural follow-up,
        //    "kya padhate hain?" style) -- act as the SAME teacher, SAME conversation_id.
        // 5. Assert: status == "no_match" -- NOT the previously-resolved data, and Rahul's new class
        //    never appears anywhere in the raw response text.
    }

    [Fact]
    public async Task A_parents_follow_up_fails_closed_after_the_parent_child_link_is_removed()
    {
        // Same shape: resolve the parent's own child, remove the ParentStudentLinks row, follow up
        // with the same conversation_id -- assert no_match, no leak.
    }

    [Fact]
    public async Task A_conversation_id_from_a_different_tenant_is_silently_treated_as_absent()
    {
        // Resolve a person as tenant A's admin, capture conversation_id. Submit that SAME
        // conversation_id as tenant B's admin with an unrelated query. Assert: no error, a completely
        // fresh classification happens (assert via a distinguishing scripted response for the fresh
        // path), and tenant A's resolved entity never appears in tenant B's response.
    }

    [Fact]
    public async Task A_conversation_id_from_a_different_user_in_the_same_tenant_is_silently_treated_as_absent()
    {
        // Same shape as above but same tenant, different user.
    }

    [Fact]
    public async Task An_explicit_Hindi_mein_batao_directive_sticks_across_a_later_English_shaped_follow_up()
    {
        // Turn 1: classifier scripted with languageDirective="hi", language="en" (the message ITSELF
        // is in English, but explicitly instructs a switch). Capture conversation_id.
        // Turn 2: classifier scripted with language="en" (a short, English-shaped follow-up, no new
        // directive). Assert: response.Language == "hi" -- the override stuck despite the per-turn
        // detection saying "en" for turn 2.
    }

    [Fact]
    public async Task Disambiguation_then_a_follow_up_resolves_against_the_stored_candidates_not_a_fresh_search()
    {
        // Seed a teacher "Rahul Sharma" (Mathematics, Class 8A/8B) and a student "Rahul Verma" in the
        // same tenant, admin caller.
        // Turn 1: PersonLookup/studentName="Rahul" -- assert needs_clarification, capture conversation_id.
        // Turn 2: PersonLookup/studentName="Rahul Sharma" (the user restates the full name) OR
        //    studentName=null with a hint-driven follow-up -- SAME conversation_id.
        // Assert: response resolves to the teacher, Status == "success".
        // Turn 3: studentName=null (a bare "kya padhate hain?" follow-up) SAME conversation_id.
        // Assert: response.Answer mentions "Mathematics".
        // Turn 4: studentName=null ("kaunsi class?") SAME conversation_id.
        // Assert: response.Answer mentions both "8A" and "8B" (or however ClassRepository.ListForTeacherAsync
        // reports the teacher's classes -- read that method's real return shape before writing this
        // assertion).
    }

    [Fact]
    public async Task An_expired_conversation_id_falls_back_to_a_fresh_query_with_no_error()
    {
        // Resolve a person, capture conversation_id. Advance past the configured TTL (use a short
        // TTL override via WebApplicationFactory settings, e.g. "AiSearch:ConversationContextTtlMinutes"
        // = "0", or manipulate the stored row's ExpiresAt directly via SQL if that's cheaper here).
        // Follow up with the same conversation_id, a DIFFERENT scripted classification (e.g. a totally
        // unrelated intent) -- assert the fresh classification's result is returned, not an error, and
        // a NEW ResolvedEntity is possible (proving the old context was genuinely dropped, not reused).
    }
}
```

Fill in each test body precisely, reusing the exact seeding/scripting/JWT helpers
`AiSearchSecurityTests.cs` already established. These are the highest-value tests in this entire plan
— do not shortcut them.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter AiSearchConversationSecurityTests -v n`
Expected: FAIL — `AiSearchService` doesn't load/save conversation context yet.

- [ ] **Step 3: Update `AiSearchRequest` binding in the controller**

In `src/Sms.Api/Controllers/AiSearchController.cs`, the `Search` action already binds
`[FromBody] AiSearchRequest request` — since `AiSearchRequest` gained a `ConversationId` field in Task
3 and the API is snake_case throughout, `conversation_id` in the request body binds automatically with
no controller code change. Just re-read the controller file to confirm nothing else references the
old 3-arg `AiSearchRequest` constructor directly (it shouldn't — model binding uses reflection, not a
direct `new` call).

- [ ] **Step 4: Rewrite `AiSearchService.SearchAsync`**

In `src/Sms.Application/Services/AiSearch/AiSearchService.cs`, inject the two new dependencies and
rewrite `SearchAsync`:

```csharp
public sealed class AiSearchService(
    IAiClassificationClient classifier,
    IAiSearchAuthorizationService authz,
    IEnumerable<IAiIntentHandler> handlers,
    IAiAnswerTemplateService templates,
    IAiSearchAuditService audit,
    IAiConversationContextStore contextStore,
    IPersonResolver personResolver,
    ITenantContext tenant,
    ITenantFeatureSet features,
    IOptions<AiSearchOptions> options) : IAiSearchService
{
    private readonly Dictionary<string, IAiIntentHandler> _handlersByIntent =
        handlers.GroupBy(h => h.Intent, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<AiSearchResponse> SearchAsync(
        AiSearchRequest request, IReadOnlyList<string> callerRoles, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.AiSearch))
            return AiSearchResponse.Fail("FeatureNotEnabled", "AI Search is not available on your plan.");

        var query = request.Query;
        if (string.IsNullOrWhiteSpace(query))
            return AiSearchResponse.Fail("InvalidRequest", "query is required.");

        var maxLength = options.Value.MaxQueryLength;
        if (query.Length > maxLength)
            return AiSearchResponse.Fail("InvalidRequest", $"query exceeds {maxLength} characters.");

        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);

        // Conversation context is a hint ONLY -- loaded before classification so it can inform the
        // classifier's phrasing understanding, but AuthorizeAsync below re-runs in full regardless,
        // and the stored ResolvedEntity is independently re-checked against that fresh result before
        // any handler is allowed to use it (see the re-authorization block after AuthorizeAsync).
        AiConversationContext? storedContext = null;
        if (Guid.TryParse(request.ConversationId, out var requestedConversationId) && tenant.TenantId is { } tid && tenant.UserId is { } uid)
            storedContext = await contextStore.LoadAsync(requestedConversationId, tid, uid, ct);

        AiConversationHint? hint = storedContext?.ResolvedEntityId is not null
            ? new AiConversationHint("the previously-discussed person", storedContext.ResolvedEntityType ?? "person")
            : null;
        // Note: the hint's EntityName here is a placeholder label, not the real name -- the real name
        // was never stored raw for re-use across turns beyond what's needed (ResolvedEntityType only
        // tells the classifier "a teacher"/"a student" was just discussed, not who). If a richer hint
        // improves classification quality in practice (e.g. the actual name), that's a candidate small
        // follow-up -- storing the name too is a one-line addition to AiConversationContext, deferred
        // here to keep this task's diff focused.

        var classification = await classifier.ClassifyAsync(query, hint, ct);
        var perTurnLanguage = classification.Language;
        var effectiveLanguage = classification.LanguageDirective ?? storedContext?.LanguageOverride ?? perTurnLanguage;
        var languageOverrideToStore = classification.LanguageDirective ?? storedContext?.LanguageOverride;

        if (string.Equals(classification.Intent, WriteIntent, StringComparison.OrdinalIgnoreCase))
            return await TerminalWithConversationAsync(
                callerRoles, query, effectiveLanguage, "WriteBlocked", classification.Intent,
                templates.RenderWriteBlocked(effectiveLanguage), "write_blocked",
                requestedConversationId: TryGetGuid(request.ConversationId), tenant, languageOverrideToStore, ct);

        if (!_handlersByIntent.TryGetValue(classification.Intent, out var handler))
            return await TerminalWithConversationAsync(
                callerRoles, query, effectiveLanguage, "Unsupported", classification.Intent,
                templates.RenderUnsupported(effectiveLanguage), "unsupported",
                requestedConversationId: TryGetGuid(request.ConversationId), tenant, languageOverrideToStore, ct);

        var auth = await authz.AuthorizeAsync(classification.Intent, classification.Filters, callerRoles, ct);
        if (!auth.Allowed)
            return await TerminalWithConversationAsync(
                callerRoles, query, effectiveLanguage, "Forbidden", classification.Intent,
                templates.RenderForbidden(effectiveLanguage), "forbidden",
                requestedConversationId: TryGetGuid(request.ConversationId), tenant, languageOverrideToStore, ct);

        // Re-authorization of the stored hint: a previously-resolved entity is used to auto-fill a
        // follow-up's target ONLY if it is still inside the scope AuthorizeAsync just (freshly)
        // computed. This is the load-bearing security check -- never skip it, never trust the stored
        // entity on its own.
        if (string.Equals(classification.Intent, "PersonLookup", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(auth.ClampedFilters.StudentName)
            && storedContext?.ResolvedEntityId is { } storedEntityId)
        {
            var stillInScope = auth.Unrestricted
                || (auth.AllowedChildStudentIds?.Contains(storedEntityId) ?? false)
                || (storedContext.ResolvedEntityType == "student" && auth.AllowedClassNames is not null
                    && await personResolver.IsStillInTeacherScopeAsync(storedEntityId, auth.AllowedClassNames, ct));

            if (!stillInScope)
            {
                if (Guid.TryParse(request.ConversationId, out var expiredId)) await contextStore.ClearAsync(expiredId, ct);
                return await TerminalWithConversationAsync(
                    callerRoles, query, effectiveLanguage, "Unsupported", classification.Intent,
                    templates.RenderNoMatch(effectiveLanguage), "no_match",
                    requestedConversationId: null, tenant, languageOverrideToStore, ct);
            }
            // Still in scope: hand the handler a synthetic AiAuthorizationResult carrying the
            // resolved entity directly, bypassing PersonResolver's name search entirely for this turn
            // (there is no name to search for -- the whole point of a follow-up).
            auth = auth with { ResolvedStudentId = storedContext.ResolvedEntityType == "student" ? storedEntityId : null };
            // PersonLookupHandler reads the resolved entity via a direct id short-circuit added in
            // this task's Step 5 (see below) -- it does not reuse ResolvedStudentId's exact original
            // meaning (that field is student-specific elsewhere); check AiAuthorizationResult's actual
            // current shape before finalizing this line, and prefer adding the minimal new field that
            // makes this clean rather than overloading an existing one with a slightly different
            // meaning, if the record's current fields don't fit.
        }
        else if (string.Equals(classification.Intent, "PersonLookup", StringComparison.OrdinalIgnoreCase)
            && storedContext?.PendingCandidates is { Count: > 0 } pending
            && string.IsNullOrWhiteSpace(auth.ClampedFilters.StudentName))
        {
            // A bare follow-up to a clarification with no new name -- not directly resolvable without
            // the user having named which candidate they meant. Leave as a fresh PersonLookup with no
            // name (handler will report no_match) UNLESS the classifier itself extracted enough to
            // narrow it -- this is a known, intentionally-simple v1 behavior: a real "the teacher"
            // style narrowing reply is Spec 2/future-work territory, not required by this plan's
            // worked examples (which all restate the name or ask a clean follow-up once ALREADY
            // resolved to one person, not while still disambiguating).
        }

        AiSearchResponse response;
        try
        {
            response = await handler.HandleAsync(auth, effectiveLanguage, page, pageSize, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await audit.LogAsync(TenantId, UserId, PrimaryRole(callerRoles), query, effectiveLanguage,
                classification.Intent, 0, false, ct);
            return AiSearchResponse.Fail("SearchFailed", "Search could not be completed. Please try again.");
        }

        await audit.LogAsync(TenantId, UserId, PrimaryRole(callerRoles), query, effectiveLanguage,
            response.Intent ?? classification.Intent, response.Count ?? 0, Audited(response), ct);

        var newConversationId = await PersistConversationAsync(
            request.ConversationId, response, classification.Intent, languageOverrideToStore, ct);

        return response with { ConversationId = newConversationId?.ToString() };
    }

    private async Task<Guid?> PersistConversationAsync(
        string? requestedConversationId, AiSearchResponse response, string intent, string? languageOverride, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid) return null;

        var update = response.ConversationUpdate;
        var context = new AiConversationContext(
            update?.ResolvedEntityId, update?.ResolvedEntityType, languageOverride, update?.PendingCandidates, intent);

        Guid.TryParse(requestedConversationId, out var existingId);
        return await contextStore.SaveAsync(
            existingId == Guid.Empty ? null : existingId, tid, uid, context, ct);
    }

    private static Guid? TryGetGuid(string? s) => Guid.TryParse(s, out var g) ? g : null;

    private const string WriteIntent = "WriteRequestDetected";

    private static bool Audited(AiSearchResponse response) => response.Success && response.Count is > 0;

    private Guid TenantId => tenant.TenantId ?? Guid.Empty;
    private Guid UserId => tenant.UserId ?? Guid.Empty;

    private static string PrimaryRole(IReadOnlyList<string> callerRoles) =>
        callerRoles.Count > 0 ? callerRoles[0] : "";

    private async Task<AiSearchResponse> TerminalWithConversationAsync(
        IReadOnlyList<string> callerRoles, string query, string language,
        string outcomeIntent, string auditedIntent, string answer, string status,
        Guid? requestedConversationId, ITenantContext tenantCtx, string? languageOverride, CancellationToken ct)
    {
        await audit.LogAsync(TenantId, UserId, PrimaryRole(callerRoles), query, language, auditedIntent, 0, false, ct);
        var response = AiSearchResponse.Terminal(language, outcomeIntent, answer, status);

        // Even a refusal renews/creates conversation state if a language override needs to persist
        // (e.g. "Hindi mein batao, delete all students" should still stick the language override for
        // the NEXT turn, even though this turn itself is WriteBlocked) -- but never for Forbidden,
        // where persisting anything about a caller's rejected attempt has no benefit and the safest
        // choice is to leave any existing context alone entirely.
        if (languageOverride is null || tenantCtx.TenantId is null || tenantCtx.UserId is null)
            return response;

        var newId = await contextStore.SaveAsync(
            requestedConversationId, tenantCtx.TenantId.Value, tenantCtx.UserId.Value,
            new AiConversationContext(null, null, languageOverride, null, auditedIntent), ct);
        return response with { ConversationId = newId.ToString() };
    }
}
```

The re-authorization check above calls `personResolver.IsStillInTeacherScopeAsync(...)` directly —
this method was added to `IPersonResolver`/`PersonResolver` in Task 7 (with its own tests,
`IsStillInTeacherScopeAsync_is_true_for_a_student_in_a_class_the_teacher_teaches` and
`...is_false_once_the_student_has_moved_to_a_different_class`), so no stub or placeholder is needed
here — `AiSearchService` simply consumes what Task 7 already built and proved.

- [ ] **Step 5: Give `PersonLookupHandler` a direct-resolved-entity short-circuit**

Re-open `src/Sms.Application/Services/AiSearch/Handlers/PersonLookupHandler.cs` (Task 9). The
re-authorization block above needs a way to tell this handler "the entity is already resolved, skip
`PersonResolver` entirely for this turn." Rather than overloading `AiAuthorizationResult.ResolvedStudentId`
(whose existing meaning elsewhere is student-specific), add a dedicated field to
`AiAuthorizationResult` itself (`AiSearchAuthorizationService.cs`, Task 6's file):

```csharp
    Guid? PreResolvedEntityId,
    string? PreResolvedEntityType,
```

(append to the existing record's parameter list; update its two private factory helpers,
`Denied`/`Allowed`, to pass `null, null` by default, and give `Allowed` an optional
`(Guid? preResolvedId = null, string? preResolvedType = null)` pair for `AiSearchService` to set on
the copy it builds in Step 4's re-authorization block, via `auth with { PreResolvedEntityId = ..., PreResolvedEntityType = ... }`
rather than plumbing it through the constructor call — a `with` expression is the minimal, correct
tool here since `AiSearchService` already has a real `AiAuthorizationResult` in hand from `authz.AuthorizeAsync`).

`PersonLookupHandler`'s constructor (Task 9) gains three more dependencies it needs for a per-type
re-fetch — `ISisService`, `StaffRepository`, and `IUserDirectoryLookup` (all already registered in DI
from earlier tasks):

```csharp
public sealed class PersonLookupHandler(
    IPersonResolver resolver, IAiAnswerTemplateService templates,
    TeacherRepository teachers, StaffRepository staff, ISisService sis, IUserDirectoryLookup users)
    : IAiIntentHandler
```

Then in `PersonLookupHandler.HandleAsync`, check the pre-resolved path first, before falling back to
the name-based resolver path:

```csharp
    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.PreResolvedEntityId is { } preResolvedId && auth.PreResolvedEntityType is { } preResolvedType)
        {
            var match = await ResolvePreResolvedAsync(preResolvedId, preResolvedType, ct);
            if (match is null)
                return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");
            var answer = await RenderAsync(match, language, ct);
            var data = new { id = match.Id, name = match.Name, type = match.Type, detail = match.Detail };
            return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false)
                with { ConversationUpdate = new AiConversationUpdate(match.Id, match.Type, null) };
        }

        var name = auth.ClampedFilters.StudentName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var matches = await resolver.ResolveAsync(name, auth, ct);

        if (matches.Count == 0)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        if (matches.Count > 1)
        {
            var candidates = matches.Select(m => new PersonCandidate(m.Name, m.Type, m.Detail)).ToList();
            var pending = matches.Select(m => new PendingCandidate(m.Id, m.Type)).ToList();
            var clarifyAnswer = templates.RenderNeedsClarification(language, candidates.Count);
            return AiSearchResponse.NeedsClarification(language, IntentName, clarifyAnswer, candidates)
                with { ConversationUpdate = new AiConversationUpdate(null, null, pending) };
        }

        var singleMatch = matches[0];
        var singleAnswer = await RenderAsync(singleMatch, language, ct);
        var singleData = new { id = singleMatch.Id, name = singleMatch.Name, type = singleMatch.Type, detail = singleMatch.Detail };
        return AiSearchResponse.Ok(language, IntentName, singleAnswer, singleData, 1, pageSize, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(singleMatch.Id, singleMatch.Type, null) };
    }

    /// Re-fetches the real, CURRENT name/detail for a conversation-context id+type -- never trusts
    /// anything about the person beyond the id+type carried in from prior context, since a name could
    /// be stale (e.g. renamed between turns) even when the entity itself is still validly in scope.
    private async Task<PersonMatch?> ResolvePreResolvedAsync(Guid id, string type, CancellationToken ct)
    {
        switch (type)
        {
            case "student":
                var student = await sis.GetStudentAsync(id, ct);
                return student.IsSuccess ? new PersonMatch(id, student.Data!.Name, "student", student.Data!.ClassLabel) : null;

            case "teacher":
                var teacherRows = await teachers.ListAsync(null, null, null, ct);
                var teacher = teacherRows.FirstOrDefault(t => t.Id == id);
                return teacher is null ? null : new PersonMatch(id, teacher.Name, "teacher", teacher.Department);

            case "staff":
                var staffRows = await staff.ListAsync(null, null, ct);
                var staffMember = staffRows.FirstOrDefault(s => s.Id == id);
                return staffMember is null ? null : new PersonMatch(id, staffMember.Name, "staff", staffMember.Department);

            default: // admin / owner / principal
                var user = await users.GetByIdAsync(id, ct);
                return user is null ? null : new PersonMatch(id, user.Name, user.Type, RoleLabelFor(user.Type));
        }
    }

    private static string RoleLabelFor(string type) => type switch
    {
        "owner" => "Owner",
        "principal" => "Principal",
        _ => "Admin",
    };
```

`teachers.ListAsync(null, null, null, ct)`/`staff.ListAsync(null, null, ct)` with a `null` query
returns every row in the tenant (confirmed by reading `TeacherRepository`/`StaffRepository`'s `q IS
NULL OR ...` WHERE clause — a `null` `q` matches everything); this is acceptable here because it's an
existing, already-tenant-scoped repository call being filtered client-side by `Id`, the same trade-off
`GreetByIdHandler` already accepts elsewhere in this codebase, not a new pattern. Add a corresponding
test to `PersonLookupHandlerTests.cs`:

```csharp
    [Fact]
    public async Task A_preresolved_teacher_renders_correctly_with_no_name_in_ClampedFilters()
    {
        // Seed a teacher "Rahul Sharma" (Mathematics). Construct an AiAuthorizationResult with
        // PreResolvedEntityId = the teacher's id, PreResolvedEntityType = "teacher",
        // ClampedFilters.StudentName = null.
        // Act: handler.HandleAsync(that auth, "en", 1, 20)
        // Assert: response.Status == "success", response.Answer contains "Mathematics",
        // response.ConversationUpdate.ResolvedEntityId == the teacher's id.
    }
```

- [ ] **Step 6: Run the security-critical tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter AiSearchConversationSecurityTests -v n`
Expected: PASS, all 6 cases. This is the load-bearing verification for this entire plan — do not
proceed to Step 7 until every case genuinely passes for the right reason (re-read each test's
assertions against what actually happened, not just a green checkmark).

- [ ] **Step 7: Run the full existing AiSearch suite for regressions**

Run: `dotnet test tests/Sms.Tests.Unit --filter FullyQualifiedName~AiSearch -v n`
Run: `dotnet test tests/Sms.Tests.Integration --filter FullyQualifiedName~AiSearch -v n`
Expected: 100% pass, zero regressions relative to the pre-this-plan baseline (70 unit / 81 integration
as of this plan's writing).

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Application/Services/AiSearch/ src/Sms.Api/Controllers/AiSearchController.cs tests/Sms.Tests.Integration/AiSearch/AiSearchConversationSecurityTests.cs
git commit -m "feat(ai-search): wire conversation_id load/save into AiSearchService with full re-authorization"
```

---

### Task 13: End-to-end worked-example tests (English, Hindi, Hinglish)

**Files:**
- Test: `tests/Sms.Tests.Integration/AiSearch/PersonLookupConversationWorkedExampleTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–12. This task adds no production code — it is the acceptance test
  for the exact worked examples in the spec and the original request.

- [ ] **Step 1: Write the English worked-example test**

Create `tests/Sms.Tests.Integration/AiSearch/PersonLookupConversationWorkedExampleTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

[Collection("sql")]
public class PersonLookupConversationWorkedExampleTests(SqlServerFixture fx)
{
    [Fact]
    public async Task English_worked_example_who_is_Rahul_what_does_he_teach_which_classes()
    {
        // Seed a teacher "Rahul Sharma" teaching Mathematics, assigned to Class 8A and Class 8B, no
        // other Rahul in the tenant. Admin caller.
        // Turn 1: script classifier PersonLookup/studentName="Rahul", language="en".
        //   Assert answer contains "Rahul Sharma is a Teacher" and "Mathematics" (single match, no
        //   clarification needed since there's only one Rahul).
        // Turn 2 (same conversation_id): script classifier PersonLookup/studentName=null, language="en"
        //   (a bare "What does he teach?").
        //   Assert answer contains "Mathematics", NOT a repeat of "is a Teacher" (this is the
        //   subject-specific follow-up render, not the full person-intro render again -- confirm
        //   which template PersonLookupHandler actually renders for a pre-resolved teacher with no
        //   new name and adjust this assertion to match your Step 3/Task 9 implementation exactly,
        //   rather than guessing the render shape here).
        // Turn 3 (same conversation_id): script classifier PersonLookup/studentName=null, language="en"
        //   ("Which classes?").
        //   Assert answer contains "Class 8A" and "Class 8B".
    }

    [Fact]
    public async Task Hindi_and_Hinglish_worked_example_matches_the_same_flow()
    {
        // Identical seeding to the English test. Script the classifier's `language` as "hi" for turn 1
        // (Rahul kaun hai?), confirm the rendered answer uses the Hindi RenderPersonIsTeacher template
        // (assert it equals the exact string that template produces for these inputs -- read Task 8's
        // final implementation to pin the real string, don't guess it here).
        // Turns 2-3 with language="hinglish" -- confirm the Hinglish template variants render.
    }

    [Fact]
    public async Task Explicit_Hindi_mein_batao_switches_and_stays_switched_through_the_whole_conversation()
    {
        // Turn 1: PersonLookup/studentName="Rahul", languageDirective="hi", language="en" (the
        // directive itself can be phrased in English -- "Hindi mein batao, Rahul kaun hai?" -- this
        // is exactly the classifier's job to detect, faked here via the scripted classification result).
        //   Assert response.Language == "hi".
        // Turn 2 (same conversation_id): a plain, English-shaped follow-up, language="en", no directive.
        //   Assert response.Language == "hi" -- STILL, because the override stuck.
        // Turn 3: an explicit switch back, languageDirective="en".
        //   Assert response.Language == "en".
        // Turn 4 (same conversation_id): language="hi" per-turn detection, no directive.
        //   Assert response.Language == "en" -- the en override from turn 3 is now what's sticking.
    }
}
```

Fill in every body precisely against the real templates/handler behavior from Tasks 8, 9, and 12 —
this is the acceptance test for the original request's own worked examples, so get the exact expected
strings right by reading the real implementation, not by re-deriving them from this plan document.

- [ ] **Step 2: Run tests to verify they fail** (if any production behavior is still incomplete)

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonLookupConversationWorkedExampleTests -v n`

If all prior tasks are correctly implemented, these may pass immediately — that's fine, this task's
job is to prove the exact worked examples work end-to-end, not to drive new implementation. If
something fails, the bug is in an earlier task; fix it there (with its own test), then return here.

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter PersonLookupConversationWorkedExampleTests -v n`
Expected: PASS (all 3 cases)

- [ ] **Step 4: Run the full repo-wide suite one final time**

Run: `dotnet test tests/Sms.Tests.Unit -v n`
Run: `dotnet test tests/Sms.Tests.Integration -v n`
Expected: 100% pass, matching the pre-this-plan repo-wide baseline (248 unit / 387 integration as of
this plan's writing) plus every new test this plan added, zero regressions.

- [ ] **Step 5: Commit**

```bash
git add tests/Sms.Tests.Integration/AiSearch/PersonLookupConversationWorkedExampleTests.cs
git commit -m "test(ai-search): add end-to-end English/Hindi/Hinglish worked-example acceptance tests"
```

---

## Self-Review Notes (for whoever executes this plan)

- **Task 12 is the highest-risk task in this plan.** It's the one place where a mid-plan design
  decision (`StudentStillInTeacherScopeAsync`, the `PreResolvedEntityId`/`PreResolvedEntityType`
  fields) is deliberately left slightly open rather than fully pre-baked, because the cleanest shape
  genuinely depends on `PersonResolver`'s exact final method signatures from Task 7. Read Task 7's
  actual committed code before starting Task 12's Step 4-5, not just this plan's Task 7 description.
- **Field names in `AiAuthorizationResult`** (`ResolvedStudentId`, `AllowedChildStudentIds`,
  `AllowedClassNames`, `Unrestricted`, `ClampedFilters`) are current as of this plan's writing
  (verified against the merged `AiSearchAuthorizationService.cs`) — if a concurrent change has altered
  this record's shape before you reach Task 6, adjust every task referencing it accordingly and note
  the discrepancy in your task's commit message.
- **Task 4's retrofit** touches roughly a dozen files mechanically — resist the urge to skip reading
  each one "since the pattern is obvious"; at least one existing handler may structure its no-match
  path differently (inline vs. private helper) and deserves a quick look before editing.
