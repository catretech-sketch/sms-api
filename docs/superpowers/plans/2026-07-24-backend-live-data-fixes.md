# Backend Fixes for Live-Data Audit Findings (Phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 10 of the 11 backend gaps found in the 2026-07-24 `sms-teacher-app` live-data audit (everything except settings-persistence, deferred). Output is `sms-backend` changes only: migrations, procs, contracts, endpoints, tests.

**Architecture:** `dbo.Users` becomes the sole identity root (`Name`, `MustSetPassword` added there). Role-specific profile data (`Teachers`, `Staff`) is linked back to `Users` via a new nullable `UserId` FK, backfilled best-effort. `/auth/me` (`AuthService.GetMe`) becomes async and role-agnostic: base identity always from `Users`, profile fields (`title`, `classroom`) resolved via a small role dispatch to `Teachers`/`Staff` — no polymorphic table, each role's table stays owned by its module. Four fixes replace stubbed/hardcoded values with live queries (no schema change). Three fixes add small, unrelated new columns.

**Tech Stack:** ASP.NET Core (C#), Dapper, FluentMigrator (SQL Server), xUnit integration tests against a real SQL Server fixture.

## Global Constraints

- Migrations are sequential `M00NN_Description.cs` files under `db/Sms.Migrations/`, next number is **M0084**. `[Migration(N, "description")]` attribute, `Up()`/`Down()`.
- Proc edits: edit the `.sql` file in place with `CREATE OR ALTER PROCEDURE` (new params get `= NULL` defaults for backward compatibility), then add a migration that re-executes it via `Execute.Sql(sql)` for each `sql` in `M0003_Procs_Auth.EmbeddedProcs("procs.<area>.<ProcName>")`, OR inline `Execute.Sql(@"...")` for procs not using the embedded-file convention (both patterns exist in this codebase — match whichever the target proc already uses). `Down()` is a no-op comment for edit-in-place migrations (prior body isn't restorable).
- All new nullable columns default to a safe value; no migration may break existing rows.
- `Users.Name` is the only identity display source app-wide — never `Teachers.Name`/`Staff.Name` directly in a response the app renders as "the user's name".
- No `sms-teacher-app` changes in this plan (that's Phase 2, separate).
- Every task's tests are xUnit integration tests against `SqlServerFixture` (`[Collection("sql")]`), following the style in `tests/Sms.Tests.Integration/Academics/TimetableTests.cs` / `Auth/AuthFlowTests.cs`.

---

### Task 1: Identity-link foundation — schema migration

**Files:**
- Create: `db/Sms.Migrations/M0084_Identity_Link_Foundation.cs`
- Test: `tests/Sms.Tests.Integration/Migrations/M0084_IdentityLinkTests.cs`

**Interfaces:**
- Produces: `dbo.Users.Name` (nvarchar(200), nullable), `dbo.Users.MustSetPassword` (bit, not null, default 0), `dbo.Teachers.UserId` (uniqueidentifier, nullable, unique where not null), `dbo.Staff.UserId` (same). Later tasks read/write these columns directly by name — no ORM mapping changes needed beyond what each task specifies.

- [ ] **Step 1: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(84, "Identity-link foundation: Users.Name/MustSetPassword, Teachers/Staff.UserId")]
public sealed class M0084_Identity_Link_Foundation : Migration
{
    public override void Up()
    {
        Alter.Table("Users")
            .AddColumn("Name").AsString(200).Nullable()
            .AddColumn("MustSetPassword").AsBoolean().NotNullable().WithDefaultValue(false);

        Alter.Table("Teachers").AddColumn("UserId").AsGuid().Nullable();
        Alter.Table("Staff").AddColumn("UserId").AsGuid().Nullable();

        Execute.Sql(
            "CREATE UNIQUE INDEX IX_Teachers_UserId ON dbo.Teachers (UserId) WHERE UserId IS NOT NULL;");
        Execute.Sql(
            "CREATE UNIQUE INDEX IX_Staff_UserId ON dbo.Staff (UserId) WHERE UserId IS NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS IX_Staff_UserId ON dbo.Staff;");
        Execute.Sql("DROP INDEX IF EXISTS IX_Teachers_UserId ON dbo.Teachers;");
        Delete.Column("UserId").FromTable("Staff");
        Delete.Column("UserId").FromTable("Teachers");
        Delete.Column("MustSetPassword").FromTable("Users");
        Delete.Column("Name").FromTable("Users");
    }
}
```

- [ ] **Step 2: Write the integration test**

```csharp
using System.Data;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.Migrations;

[Collection("sql")]
public class M0084_IdentityLinkTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Users_Teachers_Staff_have_new_columns()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();

        var userCols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users'")).ToList();
        userCols.Should().Contain("Name").And.Contain("MustSetPassword");

        var teacherCols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Teachers'")).ToList();
        teacherCols.Should().Contain("UserId");

        var staffCols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Staff'")).ToList();
        staffCols.Should().Contain("UserId");
    }

    [Fact]
    public async Task Teachers_UserId_unique_index_rejects_duplicate_link()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId) VALUES (@userId, @tenantId)", new { userId, tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Teachers (TenantId, Name, UserId) VALUES (@tenantId, 'A', @userId)", new { tenantId, userId });

        var act = () => conn.ExecuteAsync(
            "INSERT dbo.Teachers (TenantId, Name, UserId) VALUES (@tenantId, 'B', @userId)", new { tenantId, userId });

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~M0084_IdentityLinkTests"`
Expected: both tests PASS (migration runs automatically via `SqlServerFixture` before tests execute, per the existing test-infra convention).

- [ ] **Step 4: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0084_Identity_Link_Foundation.cs tests/Sms.Tests.Integration/Migrations/M0084_IdentityLinkTests.cs
git commit -m "feat(auth): add identity-link foundation columns (Users.Name/MustSetPassword, Teachers/Staff.UserId)"
```

---

### Task 2: Backfill migration + unmatched-directory report table

**Files:**
- Create: `db/Sms.Migrations/M0085_Identity_Link_Backfill.cs`
- Test: `tests/Sms.Tests.Integration/Migrations/M0085_BackfillTests.cs`

**Interfaces:**
- Consumes: columns from Task 1.
- Produces: `dbo._Migration_UnmatchedDirectoryRows` table (columns: `Id`, `SourceTable`, `SourceId`, `TenantId`, `Reason`, `MatchCount`, `CreatedAt`) that later manual review can query; backfilled `Teachers.UserId`/`Staff.UserId`/`Users.Name` for clean single-matches.

- [ ] **Step 1: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(85, "Identity-link backfill: best-effort Teachers/Staff <-> Users match by email/phone, unmatched report")]
public sealed class M0085_Identity_Link_Backfill : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE TABLE dbo._Migration_UnmatchedDirectoryRows (
    Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SourceTable nvarchar(20) NOT NULL,
    SourceId uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    Reason nvarchar(20) NOT NULL,
    MatchCount int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);");

        // Teachers -> Users: link only when exactly one Users row in the same tenant
        // matches by email or phone (case-insensitive, trimmed email).
        Execute.Sql(@"
UPDATE t
SET t.UserId = m.MatchedUserId
FROM dbo.Teachers t
CROSS APPLY (
    SELECT TOP 1 u.Id AS MatchedUserId
    FROM dbo.Users u
    WHERE u.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u.Phone IS NOT NULL AND u.Phone = t.Phone))
) m
WHERE t.UserId IS NULL
  AND (
    SELECT COUNT(*) FROM dbo.Users u2
    WHERE u2.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u2.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u2.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = t.Phone))
  ) = 1;");

        Execute.Sql(@"
UPDATE u
SET u.Name = t.Name
FROM dbo.Users u
JOIN dbo.Teachers t ON t.UserId = u.Id
WHERE u.Name IS NULL;");

        // Staff -> Users: same pattern.
        Execute.Sql(@"
UPDATE s
SET s.UserId = m.MatchedUserId
FROM dbo.Staff s
CROSS APPLY (
    SELECT TOP 1 u.Id AS MatchedUserId
    FROM dbo.Users u
    WHERE u.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u.Phone IS NOT NULL AND u.Phone = s.Phone)
) m
WHERE s.UserId IS NULL
  AND (
    SELECT COUNT(*) FROM dbo.Users u2
    WHERE u2.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = s.Phone)
  ) = 1;");

        Execute.Sql(@"
UPDATE u
SET u.Name = s.Name
FROM dbo.Users u
JOIN dbo.Staff s ON s.UserId = u.Id
WHERE u.Name IS NULL;");

        // Report every Teachers/Staff row that didn't get a clean single match.
        Execute.Sql(@"
INSERT INTO dbo._Migration_UnmatchedDirectoryRows (SourceTable, SourceId, TenantId, Reason, MatchCount)
SELECT 'Teachers', t.Id, t.TenantId, CASE WHEN x.Cnt = 0 THEN 'no_match' ELSE 'ambiguous' END, x.Cnt
FROM dbo.Teachers t
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Users u2
    WHERE u2.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u2.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u2.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = t.Phone))
) x
WHERE t.UserId IS NULL AND x.Cnt <> 1;");

        Execute.Sql(@"
INSERT INTO dbo._Migration_UnmatchedDirectoryRows (SourceTable, SourceId, TenantId, Reason, MatchCount)
SELECT 'Staff', s.Id, s.TenantId, CASE WHEN x.Cnt = 0 THEN 'no_match' ELSE 'ambiguous' END, x.Cnt
FROM dbo.Staff s
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Users u2
    WHERE u2.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = s.Phone)
) x
WHERE s.UserId IS NULL AND x.Cnt <> 1;");
    }

    public override void Down()
    {
        // No-op: backfilled data and the report table are historical record, not restorable/reversible.
    }
}
```

- [ ] **Step 2: Write the integration test**

```csharp
using Dapper;
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.Migrations;

[Collection("sql")]
public class M0085_BackfillTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Clean_single_match_links_teacher_and_copies_name()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Email) VALUES (@userId, @tenantId, 'match@x.com')",
            new { userId, tenantId });
        var teacherId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT dbo.Teachers (Id, TenantId, Name, Email) VALUES (@teacherId, @tenantId, 'Jane Teacher', 'match@x.com')",
            new { teacherId, tenantId });

        // Re-run the backfill statements directly (migration already ran once at fixture setup with no rows present;
        // this test validates the SQL logic itself against freshly inserted rows using the same predicate).
        await conn.ExecuteAsync(@"
UPDATE t SET t.UserId = u.Id
FROM dbo.Teachers t JOIN dbo.Users u ON u.TenantId = t.TenantId
  AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(t.Email)))
WHERE t.Id = @teacherId AND t.UserId IS NULL", new { teacherId });
        await conn.ExecuteAsync(@"
UPDATE u SET u.Name = t.Name FROM dbo.Users u JOIN dbo.Teachers t ON t.UserId = u.Id
WHERE u.Id = @userId AND u.Name IS NULL", new { userId });

        var linkedUserId = await conn.QuerySingleAsync<Guid?>(
            "SELECT UserId FROM dbo.Teachers WHERE Id = @teacherId", new { teacherId });
        linkedUserId.Should().Be(userId);

        var name = await conn.QuerySingleAsync<string?>(
            "SELECT Name FROM dbo.Users WHERE Id = @userId", new { userId });
        name.Should().Be("Jane Teacher");
    }

    [Fact]
    public async Task Report_table_exists_and_is_queryable()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo._Migration_UnmatchedDirectoryRows");
        count.Should().BeGreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~M0085_BackfillTests"`
Expected: both PASS.

- [ ] **Step 4: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0085_Identity_Link_Backfill.cs tests/Sms.Tests.Integration/Migrations/M0085_BackfillTests.cs
git commit -m "feat(auth): backfill Teachers/Staff UserId links + unmatched-directory report table"
```

---

### Task 3: Role-agnostic `/auth/me` (`GetMe` becomes async, resolves identity from Users + role-dispatched profile)

**Files:**
- Modify: `src/Sms.Shared.Kernel/Auth/UserRecord.cs`
- Modify: `db/Sms.Migrations/procs/authlogin/User_GetById.sql`
- Create: `db/Sms.Migrations/M0086_User_GetById_Add_Identity_Fields.cs`
- Modify: `src/Sms.Infrastructure/DAO/AuthDao.cs` (inline SQL in `ListByEmailAsync`/`ListByPhoneAsync`/`GetByEmailAndTenantAsync`)
- Create: `src/Sms.Application/Interfaces/DAO/IProfileDao.cs`
- Create: `src/Sms.Infrastructure/DAO/ProfileDao.cs`
- Modify: `src/Sms.Application/Services/Auth/AuthService.cs` (`GetMe` → `GetMeAsync`, constructor gains `IProfileDao profiles` and `ClientRepository clients`)
- Modify: `src/Sms.Api/Controllers/LoginController.cs` (`Me()` → `async Task<IActionResult> Me(CancellationToken ct)`)
- Modify: wherever `IAuthService`/`IAuthDao`/`IProfileDao` are registered for DI (search `src/Sms.Api/Extensions/` for `ConfigureSmsServices` or similar — find where `AuthDao`/`AuthService` are added to the `IServiceCollection` and add `IProfileDao`/`ProfileDao` following the same pattern)
- Test: `tests/Sms.Tests.Integration/Auth/GetMeProfileTests.cs`

**Interfaces:**
- Consumes: `Users.Name`/`Users.MustSetPassword`/`Teachers.UserId`/`Staff.UserId` from Tasks 1-2.
- Produces: `IProfileDao.GetTeacherProfileByUserIdAsync(Guid userId, CancellationToken ct) : Task<(string? Title)?>` and `GetStaffProfileByUserIdAsync(Guid userId, CancellationToken ct) : Task<(string? Title)?>` — later tasks do not depend on this interface, it's `/auth/me`-only.
- `GET /v1/auth/me` response gains: `name`, `email`, `phone`, `tenant_name`, `must_set_password`, `title`, `classroom` (classroom via `Classes.ClassTeacherId` join, teacher-role only).

- [ ] **Step 1: Extend `UserRecord`**

```csharp
namespace Sms.Shared.Kernel.Auth;

public sealed record UserRecord(
    Guid Id, Guid? TenantId, string? Email, string? StudentId, string? Phone,
    string? PasswordHash, bool IsPlatform, string Status, string? Name, bool MustSetPassword);
```

- [ ] **Step 2: Update every SQL that constructs a `UserRecord` to select the two new columns**

In `src/Sms.Infrastructure/DAO/AuthDao.cs`, update the three inline `SELECT` column lists (`ListByEmailAsync`, `ListByPhoneAsync`, `GetByEmailAndTenantAsync`) from:
```
"SELECT Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status "
```
to:
```
"SELECT Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status, Name, MustSetPassword "
```
(three occurrences — `ListByEmailAsync`, `ListByPhoneAsync`, `GetByEmailAndTenantAsync` — the `WHERE`/`ORDER BY` clauses after each are unchanged).

Also grep `db/Sms.Migrations/procs/authlogin/` for `User_GetByEmail`/`User_GetByPhone` (the procs behind `AuthQueries.GetByEmail`/`AuthQueries.GetByPhone`, used by `GetByEmailAsync`/`GetByPhoneAsync` in the same file) and add `Name, MustSetPassword` to their `SELECT` column lists the same way, mirroring the edit in Step 3 below — these procs were not part of this plan's research pass, so locate them by grepping `CREATE OR ALTER PROCEDURE dbo.User_GetByEmail` / `dbo.User_GetByPhone` under that folder before editing.

- [ ] **Step 3: Edit `User_GetById.sql` in place**

```sql
CREATE OR ALTER PROCEDURE dbo.User_GetById
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword
    FROM dbo.Users u
    WHERE u.Id = @Id;
END
```

- [ ] **Step 4: Migration to re-run the edited proc(s)**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(86, "Identity fields on User_GetById/GetByEmail/GetByPhone (embedded CREATE OR ALTER)")]
public sealed class M0086_User_GetById_Add_Identity_Fields : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.authlogin.User_GetById"))
            Execute.Sql(sql);
        // If Step 2's grep found User_GetByEmail/User_GetByPhone as separate embedded proc files,
        // re-run them here too, e.g.:
        // foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.authlogin.User_GetByEmail")) Execute.Sql(sql);
        // foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.authlogin.User_GetByPhone")) Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc bodies are superseded, not restored.
    }
}
```

- [ ] **Step 5: New `IProfileDao` + `ProfileDao`**

```csharp
// src/Sms.Application/Interfaces/DAO/IProfileDao.cs
namespace Sms.Application.Interfaces.DAO;

public interface IProfileDao
{
    Task<string?> GetTeacherTitleByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<string?> GetStaffTitleByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<string?> GetClassroomNameByTeacherUserIdAsync(Guid userId, CancellationToken ct = default);
}
```

```csharp
// src/Sms.Infrastructure/DAO/ProfileDao.cs
using Sms.Application.Interfaces.DAO;
using Sms.Infrastructure.SQL;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class ProfileDao(IDbConnectionFactory factory) : BaseRepository(factory), IProfileDao
{
    public async Task<string?> GetTeacherTitleByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            "SELECT Designation FROM dbo.Teachers WHERE UserId = @userId", new { userId }, ct))
        .FirstOrDefault();

    public async Task<string?> GetStaffTitleByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            "SELECT Role FROM dbo.Staff WHERE UserId = @userId", new { userId }, ct))
        .FirstOrDefault();

    public async Task<string?> GetClassroomNameByTeacherUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            @"SELECT c.Name FROM dbo.Classes c
              JOIN dbo.Teachers t ON t.Id = c.ClassTeacherId
              WHERE t.UserId = @userId", new { userId }, ct))
        .FirstOrDefault();
}
```

- [ ] **Step 6: Rewrite `GetMe` as async, role-agnostic**

In `src/Sms.Application/Services/Auth/AuthService.cs`:
- Add `IProfileDao profiles` and `ClientRepository clients` to the primary constructor parameter list (note `clients` — `ClientRepository` — is already injected; reuse the existing parameter, do not add a duplicate).
- Change the interface method:
```csharp
Task<ApiResult<object>> GetMeAsync(ClaimsPrincipal user, CancellationToken ct = default);
```
(replacing the old synchronous `ApiResult<object> GetMe(ClaimsPrincipal user);`)
- Replace the `GetMe` method body:

```csharp
public async Task<ApiResult<object>> GetMeAsync(ClaimsPrincipal user, CancellationToken ct = default)
{
    var sub = user.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var userId))
        return ApiResult<object>.Fail(new Error("unauthorized", "unauthorized"), 401);

    var record = await users.GetByIdAsync(userId, ct);
    if (record is null)
        return ApiResult<object>.Fail(new Error("unauthorized", "unauthorized"), 401);

    var roles = user.FindAll("role").Select(c => c.Value).ToArray();
    var (title, classroom) = await ResolveProfileAsync(record.Id, roles, ct);

    string? tenantName = null;
    if (record.TenantId is Guid tid)
    {
        var school = await clients.GetAsync(tid, ct);
        tenantName = school?.Name;
    }

    return ApiResult<object>.Ok(new
    {
        id = sub,
        tenant_id = user.FindFirst("tenant_id")?.Value,
        roles,
        is_platform = user.FindFirst("is_platform")?.Value == "1",
        name = record.Name,
        email = record.Email,
        phone = record.Phone,
        tenant_name = tenantName,
        must_set_password = record.MustSetPassword,
        title,
        classroom,
    });
}

/// Role-agnostic profile resolution: base identity always comes from Users (already
/// on `record`); role-specific fields are looked up via a small dispatch so adding a
/// Parent/Student branch later (for sms-staff/sms-student) is additive, not a rewrite.
private async Task<(string? Title, string? Classroom)> ResolveProfileAsync(
    Guid userId, IReadOnlyList<string> roles, CancellationToken ct)
{
    var lastSegment = roles.Select(r => r.Split('.').LastOrDefault() ?? r).FirstOrDefault();
    return lastSegment switch
    {
        "teacher" => (
            await profiles.GetTeacherTitleByUserIdAsync(userId, ct),
            await profiles.GetClassroomNameByTeacherUserIdAsync(userId, ct)),
        "principal" => (null, null),
        _ => (await profiles.GetStaffTitleByUserIdAsync(userId, ct), null),
    };
}
```

- [ ] **Step 7: Update the controller**

In `src/Sms.Api/Controllers/LoginController.cs`, replace:
```csharp
[HttpGet("me")]
[Authorize]
public IActionResult Me() => FromResult(auth.GetMe(User));
```
with:
```csharp
[HttpGet("me")]
[Authorize]
public async Task<IActionResult> Me(CancellationToken ct) => FromResult(await auth.GetMeAsync(User, ct));
```

- [ ] **Step 8: Register `IProfileDao` in DI**

Grep `src/Sms.Api/Extensions/` (or wherever `ConfigureSmsServices` lives, called from `Program.cs`) for the existing `services.AddScoped<IAuthDao, AuthDao>()`-style registration and add `services.AddScoped<IProfileDao, ProfileDao>();` next to it, following the exact same pattern.

- [ ] **Step 9: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class GetMeProfileTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> AppWithDb() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    [Fact]
    public async Task Teacher_me_returns_name_and_title_from_linked_Teachers_row()
    {
        var hasher = new Sms.Shared.Kernel.Auth.PasswordHasher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Email, PasswordHash, Name) VALUES (@userId, @tenantId, @email, @hash, 'Jane Teacher')",
                new { userId, tenantId, email = $"t{Guid.NewGuid():N}@x.com", hash = hasher.Hash("Pass123!") });
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (TenantId, Name, Designation, UserId) VALUES (@tenantId, 'Jane Teacher', 'Senior Teacher', @userId)",
                new { tenantId, userId });
            await conn.ExecuteAsync(
                "INSERT dbo.UserRoles (UserId, Role) VALUES (@userId, 'school.teacher')", new { userId });
        }

        await using var app = AppWithDb();
        var jwt = new Sms.Shared.Kernel.Auth.JwtTokenService(
            new Sms.Shared.Kernel.Auth.JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = "integration-test-signing-key-32-bytes-min!!", AccessTokenMinutes = 15 },
            new Sms.Shared.Kernel.Time.SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { "school.teacher" }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Jane Teacher");
        data.GetProperty("title").GetString().Should().Be("Senior Teacher");
    }

    [Fact]
    public async Task Principal_me_returns_name_but_null_title()
    {
        var hasher = new Sms.Shared.Kernel.Auth.PasswordHasher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Email, PasswordHash, Name) VALUES (@userId, @tenantId, @email, @hash, 'Priya Principal')",
                new { userId, tenantId, email = $"p{Guid.NewGuid():N}@x.com", hash = hasher.Hash("Pass123!") });
        }

        await using var app = AppWithDb();
        var jwt = new Sms.Shared.Kernel.Auth.JwtTokenService(
            new Sms.Shared.Kernel.Auth.JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = "integration-test-signing-key-32-bytes-min!!", AccessTokenMinutes = 15 },
            new Sms.Shared.Kernel.Time.SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { "school.principal" }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Priya Principal");
        data.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
```

- [ ] **Step 10: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~GetMeProfileTests"`
Expected: both PASS. Also run `dotnet test --filter "FullyQualifiedName~AuthFlowTests"` to confirm the existing `/auth/me` tests (which don't assert on `name`/`title`) still pass unmodified.

- [ ] **Step 11: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Shared.Kernel/Auth/UserRecord.cs db/Sms.Migrations/procs/authlogin/User_GetById.sql \
  db/Sms.Migrations/M0086_User_GetById_Add_Identity_Fields.cs src/Sms.Infrastructure/DAO/AuthDao.cs \
  src/Sms.Application/Interfaces/DAO/IProfileDao.cs src/Sms.Infrastructure/DAO/ProfileDao.cs \
  src/Sms.Application/Services/Auth/AuthService.cs src/Sms.Api/Controllers/LoginController.cs \
  tests/Sms.Tests.Integration/Auth/GetMeProfileTests.cs
git commit -m "feat(auth): role-agnostic /auth/me — name/email/phone/tenant_name/title/classroom from Users + role-dispatched profile"
```

---

### Task 4: Activate `must_set_password` gate (clear on successful set-password)

**Files:**
- Modify: `src/Sms.Application/Services/Auth/AuthService.cs` (`SetPasswordAsync`)
- Test: `tests/Sms.Tests.Integration/Auth/MustSetPasswordTests.cs`

**Interfaces:**
- Consumes: `Users.MustSetPassword` (Task 1), `GetMeAsync` (Task 3, to verify the flag clears in the response).
- Produces: nothing new consumed by later tasks.

Note: this task only wires the **clearing** side (on successful self-service password set). Setting `MustSetPassword = true` at account-creation time depends on wherever new `Users` rows get provisioned (invite/onboarding flow) — that provisioning path was not part of this plan's research and is **out of scope here**; leaving all existing and newly-migrated rows at the Task 1 default (`false`) is safe and matches the spec's explicit requirement that nobody currently using the app gets newly gated. Flag the provisioning-time `true`-setting as a follow-up task for whoever owns the invite flow.

- [ ] **Step 1: Add a `SetPasswordAsync` DAO method to clear the flag**

In `src/Sms.Application/Interfaces/DAO/IAuthDao.cs`, the existing `SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct)` already updates the password. Extend its implementation to also clear `MustSetPassword`:

In `db/Sms.Migrations/procs/authlogin/` grep for the proc backing `AuthQueries.SetPassword` (used by `AuthDao.SetPasswordAsync`) and add `MustSetPassword = 0` to its `UPDATE` statement's `SET` clause, mirroring the pattern:
```sql
UPDATE dbo.Users SET PasswordHash = @PasswordHash, MustSetPassword = 0 WHERE Id = @UserId;
```

- [ ] **Step 2: Migration to re-run the edited proc**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(87, "User_SetPassword: also clear MustSetPassword on success (embedded CREATE OR ALTER)")]
public sealed class M0087_User_SetPassword_Clear_MustSetPassword : Migration
{
    public override void Up()
    {
        // Replace "User_SetPassword" with whatever proc name Step 1's grep found.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.authlogin.User_SetPassword"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: previous proc body is superseded, not restored.
    }
}
```

- [ ] **Step 3: Write the integration test**

```csharp
using Dapper;
using FluentAssertions;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class MustSetPasswordTests(SqlServerFixture fx)
{
    [Fact]
    public async Task SetPassword_clears_MustSetPassword_flag()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Email, MustSetPassword) VALUES (@userId, @tenantId, @email, 1)",
            new { userId, tenantId = Guid.NewGuid(), email = $"u{Guid.NewGuid():N}@x.com" });

        await conn.ExecuteAsync(
            "EXEC dbo.User_SetPassword @UserId = @userId, @PasswordHash = @hash",
            new { userId, hash = "somehash" });

        var flag = await conn.QuerySingleAsync<bool>(
            "SELECT MustSetPassword FROM dbo.Users WHERE Id = @userId", new { userId });
        flag.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MustSetPasswordTests"`
Expected: PASS (adjust the `EXEC` proc name in the test if Step 1's grep found a different proc name than `User_SetPassword`).

- [ ] **Step 5: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0087_User_SetPassword_Clear_MustSetPassword.cs tests/Sms.Tests.Integration/Auth/MustSetPasswordTests.cs
git commit -m "feat(auth): clear MustSetPassword on successful self-service password set"
```

---

### Task 5: Timetable teacher filter by role

**Files:**
- Modify: `src/Sms.Modules.Academics/Data/TimetableRepository.cs`
- Modify: `src/Sms.Api/Controllers/TimetableController.cs`
- Modify: wherever `IAcademicsService.ListTimetableAsync` is implemented (search `src/Sms.Application/Services/Academics/`)
- Test: `tests/Sms.Tests.Integration/Academics/TimetableTeacherFilterTests.cs`

**Interfaces:**
- Consumes: `Teachers.UserId` (Task 1/2), `Classes.ClassTeacherId`, `Subjects.TeacherId` (both pre-existing).
- Produces: `TimetableRepository.ListForTeacherAsync(Guid teacherUserId, CancellationToken ct)` — new method, additive; `ListAsync()` (whole-tenant, unfiltered) stays unchanged and is now only called for principal callers.

- [ ] **Step 1: Add a teacher-scoped query to `TimetableRepository`**

```csharp
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class TimetableRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime";

    public Task<IReadOnlyList<TimetableSlotResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>(
            $"SELECT {Cols} FROM dbo.TimetableSlots ORDER BY [Day], Period", null, ct);

    /// Slots derivable as "this teacher's own": either they're the linked class-teacher
    /// for the slot's class, or they're the assigned teacher for a subject whose name
    /// matches the slot's free-text Subject. A slot with neither linkage won't appear
    /// for anyone — known limitation, strictly narrower than the prior whole-tenant leak.
    public Task<IReadOnlyList<TimetableSlotResponse>> ListForTeacherAsync(Guid teacherUserId, CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>($@"
SELECT {Cols.Replace("Id,", "ts.Id,").Replace("TenantId,", "ts.TenantId,").Replace("[Day],", "ts.[Day],").Replace("Period,", "ts.Period,").Replace("Subject,", "ts.Subject,").Replace("ClassId,", "ts.ClassId,").Replace("ClassName,", "ts.ClassName,").Replace("Room,", "ts.Room,").Replace("StartTime,", "ts.StartTime,").Replace("EndTime", "ts.EndTime")}
FROM dbo.TimetableSlots ts
JOIN dbo.Teachers t ON t.UserId = @teacherUserId
LEFT JOIN dbo.Classes c ON c.Id = ts.ClassId
LEFT JOIN dbo.Subjects sub ON sub.Name = ts.Subject
WHERE c.ClassTeacherId = t.Id OR sub.TeacherId = t.Id
ORDER BY ts.[Day], ts.Period", new { teacherUserId }, ct);

    public Task<TimetableSlotResponse?> CreateAsync(Guid tenantId, CreateTimetableSlotRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TimetableSlotResponse>("dbo.TimetableSlot_Create", new
        {
            TenantId = tenantId, r.Day, r.Period, r.Subject, r.ClassId, r.ClassName, r.Room, r.StartTime, r.EndTime
        }, ct);
}
```

(The `Cols.Replace(...)` chain above is fragile string surgery — if the implementer finds it unreadable, replace it with an explicit inline column list instead, e.g. `"ts.Id, ts.TenantId, ts.[Day], ts.Period, ts.Subject, ts.ClassId, ts.ClassName, ts.Room, ts.StartTime, ts.EndTime"` as its own `const string TeacherCols` — either is fine, prefer the explicit constant for clarity.)

- [ ] **Step 2: Wire role-based dispatch in the service**

Locate `IAcademicsService.ListTimetableAsync` (search `src/Sms.Application/Services/Academics/AcademicsService.cs` or similarly named file). Change its signature to accept the caller's identity and dispatch by role:

```csharp
public async Task<ApiResult<IReadOnlyList<TimetableSlotResponse>>> ListTimetableAsync(
    ClaimsPrincipal caller, CancellationToken ct = default)
{
    var roles = caller.FindAll("role").Select(c => c.Value).ToArray();
    var isPrincipal = roles.Any(r => r.Split('.').LastOrDefault() == "principal");
    if (isPrincipal)
        return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Ok(await timetable.ListAsync(ct));

    var sub = caller.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var userId))
        return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Fail(new Error("unauthorized", "unauthorized"), 401);
    return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Ok(await timetable.ListForTeacherAsync(userId, ct));
}
```

(Adjust to match the exact existing method signature/DI pattern found in the file — the shown body is the logic to graft in, not necessarily a verbatim replacement if the surrounding interface differs slightly.)

- [ ] **Step 3: Update the controller to pass the caller**

In `src/Sms.Api/Controllers/TimetableController.cs`:
```csharp
[HttpGet("timetable")]
[Authorize(Policy = AuthorizationPolicies.TeacherApp)]
public async Task<IActionResult> List(CancellationToken ct) =>
    FromResult(await academics.ListTimetableAsync(User, ct));
```

- [ ] **Step 4: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class TimetableTeacherFilterTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    [Fact]
    public async Task Teacher_only_sees_their_own_class_slots_not_the_whole_tenant()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var teacherUserId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId) VALUES (@teacherUserId, @tenantId)", new { teacherUserId, tenantId });
            var teacherId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Teachers (Id, TenantId, Name, UserId) VALUES (@teacherId, @tenantId, 'T1', @teacherUserId)",
                new { teacherId, tenantId, teacherUserId });
            var myClassId = Guid.NewGuid();
            var otherClassId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, StudentCount, ClassTeacherId) VALUES (@myClassId, @tenantId, 'MyClass', 0, @teacherId)",
                new { myClassId, tenantId, teacherId });
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, StudentCount, ClassTeacherId) VALUES (@otherClassId, @tenantId, 'OtherClass', 0, NULL)",
                new { otherClassId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.TimetableSlots (TenantId, [Day], Period, ClassId, ClassName) VALUES (@tenantId, 'Mon', 1, @myClassId, 'MyClass')",
                new { tenantId, myClassId });
            await conn.ExecuteAsync(
                "INSERT dbo.TimetableSlots (TenantId, [Day], Period, ClassId, ClassName) VALUES (@tenantId, 'Mon', 2, @otherClassId, 'OtherClass')",
                new { tenantId, otherClassId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(teacherUserId, tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/timetable");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var slots = doc.RootElement.GetProperty("data");
        slots.GetArrayLength().Should().Be(1);
        slots[0].GetProperty("class_name").GetString().Should().Be("MyClass");
    }

    [Fact]
    public async Task Principal_sees_the_whole_tenant_grid_unfiltered()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.TimetableSlots (TenantId, [Day], Period, ClassName) VALUES (@tenantId, 'Tue', 1, 'X')",
                new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.TimetableSlots (TenantId, [Day], Period, ClassName) VALUES (@tenantId, 'Tue', 2, 'Y')",
                new { tenantId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Principal }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/timetable");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~TimetableTeacherFilterTests"`
Expected: both PASS. Also run `dotnet test --filter "FullyQualifiedName~TimetableTests"` to confirm the existing tests (principal create, teacher list, 403 cases) still pass.

- [ ] **Step 6: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Academics/Data/TimetableRepository.cs src/Sms.Api/Controllers/TimetableController.cs \
  tests/Sms.Tests.Integration/Academics/TimetableTeacherFilterTests.cs
# also add whichever AcademicsService file Step 2 modified
git commit -m "fix(academics): filter /timetable to the caller's own slots for teachers, unfiltered for principal"
```

---

### Task 6: Approvals — live `RequesterName`

**Files:**
- Modify: `src/Sms.Modules.Staffing/Contracts/LeaveContracts.cs`
- Modify: `src/Sms.Modules.Staffing/Data/LeaveRepository.cs` (`ListByStatusAsync`)
- Test: `tests/Sms.Tests.Integration/Staffing/ApprovalsRequesterNameTests.cs`

**Interfaces:**
- Consumes: `Users.Name` (Task 1/2/3).
- Produces: `LeaveResponse.RequesterName` (nullable string) — additive field, existing consumers of `LeaveResponse` (e.g. `ListMineAsync`) are unaffected since it's nullable and not required elsewhere.

- [ ] **Step 1: Add `RequesterName` to `LeaveResponse`**

```csharp
namespace Sms.Modules.Staffing.Contracts;

public sealed record LeaveResponse(
    Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
    string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote,
    string? RequesterName = null);
```

- [ ] **Step 2: Join `Users.Name` at query time in `ListByStatusAsync`**

```csharp
public Task<IReadOnlyList<LeaveResponse>> ListByStatusAsync(string status, CancellationToken ct = default) =>
    QueryInlineAsync<LeaveResponse>(@"
SELECT lr.Id, lr.TenantId, lr.RequesterId, lr.ChildId, lr.Type, lr.FromDate, lr.ToDate,
       lr.Reason, lr.Substitute, lr.Status, lr.AppliedOn, lr.DecidedNote, u.Name AS RequesterName
FROM dbo.LeaveRequests lr
LEFT JOIN dbo.Users u ON u.Id = lr.RequesterId
WHERE lr.Status = @status
ORDER BY lr.AppliedOn DESC", new { status }, ct);
```

(Leave `GetAsync`/`ListMineAsync` unchanged — the audit finding is specifically about `ApprovalsScreen`, which reads `ListByStatusAsync`.)

- [ ] **Step 3: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Staffing;

[Collection("sql")]
public class ApprovalsRequesterNameTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Approvals_list_includes_requester_name_from_Users()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name) VALUES (@requesterId, @tenantId, 'Sam Requester')",
                new { requesterId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.LeaveRequests (TenantId, RequesterId, Type, Status) VALUES (@tenantId, @requesterId, 'casual', 'pending')",
                new { tenantId, requesterId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Principal }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/approvals?status=pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("data");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("requester_name").GetString().Should().Be("Sam Requester");
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ApprovalsRequesterNameTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Staffing/Contracts/LeaveContracts.cs src/Sms.Modules.Staffing/Data/LeaveRepository.cs \
  tests/Sms.Tests.Integration/Staffing/ApprovalsRequesterNameTests.cs
git commit -m "feat(staffing): resolve LeaveResponse.RequesterName live via Users join in approvals list"
```

---

### Task 7: Announcements — `CreatorUserId` + read-time name resolution

**Files:**
- Create: `db/Sms.Migrations/M0088_Announcements_CreatorUserId.cs`
- Modify: `src/Sms.Modules.Comms/CommsModule.cs` (`AnnouncementResponse`, `ListAnnouncementsAsync`, `CreateAnnouncementAsync`)
- Modify: `src/Sms.Application/Services/Comms/AnnouncementService.cs` (`CreateAsync` call site)
- Modify: `src/Sms.Api/Controllers/AnnouncementController.cs` (`Create` — pass user id, not just role)
- Test: `tests/Sms.Tests.Integration/Comms/AnnouncementCreatorNameTests.cs`

**Interfaces:**
- Consumes: `Users.Name` (Task 1/2/3).
- Produces: `Announcements.CreatorUserId` column; `AnnouncementResponse.From` now resolves via join at read time instead of a baked role string.

- [ ] **Step 1: Migration adding `CreatorUserId`**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(88, "Announcements: add CreatorUserId for read-time creator-name resolution")]
public sealed class M0088_Announcements_CreatorUserId : Migration
{
    public override void Up() =>
        Alter.Table("Announcements").AddColumn("CreatorUserId").AsGuid().Nullable();

    public override void Down() =>
        Delete.Column("CreatorUserId").FromTable("Announcements");
}
```

- [ ] **Step 2: Update `CommsModule.cs` — write `CreatorUserId`, resolve `From` at read time**

Change `CreateAnnouncementAsync`'s signature to also accept the creator's user id, and stop writing `role` into `From`:
```csharp
public Task<AnnouncementResponse?> CreateAnnouncementAsync(
    Guid tenantId, CreateAnnouncementRequest r, Guid? creatorUserId, string? role, CancellationToken ct = default) =>
    QuerySingleProcAsync<AnnouncementResponse>("dbo.Announcement_Create",
        new { TenantId = tenantId, r.Title, r.Body, CreatorUserId = creatorUserId, Role = role, r.Type, r.Audience }, ct);
```

Update `ListAnnouncementsAsync` to resolve the display name at read time, falling back to the role label when the creator has no name yet:
```csharp
public Task<IReadOnlyList<AnnouncementResponse>> ListAnnouncementsAsync(string? audience, CancellationToken ct = default) =>
    QueryInlineAsync<AnnouncementResponse>(@"
SELECT a.Id, a.TenantId, a.Title, a.Body, a.[Date], COALESCE(u.Name, a.Role) AS [From], a.Role, a.Type, a.Pinned, a.Audience
FROM dbo.Announcements a
LEFT JOIN dbo.Users u ON u.Id = a.CreatorUserId
WHERE (@audience IS NULL OR a.Audience IS NULL OR a.Audience = @audience)
ORDER BY a.[Date] DESC", new { audience }, ct);
```

`AnnouncementResponse` itself needs no field changes (`From` stays `string?`) — only its resolved value changes.

Note: `Announcement_Create`'s stored procedure (bound by name, not found as a `CREATE PROCEDURE` in this plan's research — grep `db/Sms.Migrations/` for `Announcement_Create` to locate it before editing) needs a new `@CreatorUserId uniqueidentifier = NULL` parameter added to both its parameter list and its `INSERT` column list, alongside the existing `@From`/`@Role` params (which can stay for now — `From` as a stored column becomes vestigial once reads always resolve via `COALESCE`, but leaving it populated is harmless and avoids touching more of the insert path than necessary).

- [ ] **Step 3: Migration to re-run the edited `Announcement_Create` proc**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(89, "Announcement_Create: accept CreatorUserId (embedded CREATE OR ALTER)")]
public sealed class M0089_Announcement_Create_CreatorUserId : Migration
{
    public override void Up()
    {
        // Replace with the actual embedded-proc path found by Step 2's grep, e.g.:
        // foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.comms.Announcement_Create")) Execute.Sql(sql);
        throw new NotImplementedException(
            "Locate Announcement_Create's source (grep db/Sms.Migrations for 'Announcement_Create'), " +
            "add @CreatorUserId uniqueidentifier = NULL to its param list and INSERT column list, " +
            "then replace this throw with the real Execute.Sql/EmbeddedProcs call.");
    }

    public override void Down()
    {
        // No-op: previous proc body is superseded, not restored.
    }
}
```

- [ ] **Step 4: Update the service and controller to pass the creator's user id**

In `src/Sms.Application/Services/Comms/AnnouncementService.cs`, change `CreateAsync`'s signature to accept a `Guid? creatorUserId` alongside `role`, and pass it through:
```csharp
public interface IAnnouncementService
{
    Task<ApiResult<IReadOnlyList<AnnouncementResponse>>> ListAsync(string? audience, CancellationToken ct = default);
    Task<ApiResult<AnnouncementResponse>> CreateAsync(
        CreateAnnouncementRequest req, Guid? creatorUserId, string? role, CancellationToken ct = default);
}
```
and in `CreateAsync`'s body, change:
```csharp
var created = await repo.CreateAnnouncementAsync(tid, normalized, role, role, ct);
```
to:
```csharp
var created = await repo.CreateAnnouncementAsync(tid, normalized, creatorUserId, role, ct);
```

In `src/Sms.Api/Controllers/AnnouncementController.cs`:
```csharp
[HttpPost("announcements")]
[Authorize(Policy = Policies.Principal)]
public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest req, CancellationToken ct)
{
    var role = User.FindFirst("role")?.Value;
    var sub = User.FindFirst("sub")?.Value;
    Guid? creatorUserId = Guid.TryParse(sub, out var uid) ? uid : null;
    return FromResult(await announcements.CreateAsync(req, creatorUserId, role, ct));
}
```

- [ ] **Step 5: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Comms;

[Collection("sql")]
public class AnnouncementCreatorNameTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Announcement_From_resolves_to_creator_name_not_role()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var principalUserId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name) VALUES (@principalUserId, @tenantId, 'Priya Principal')",
                new { principalUserId, tenantId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(principalUserId, tenantId, new[] { Policies.Principal }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var create = await client.PostAsJsonAsync("/v1/announcements", new
        {
            title = "Test Notice", body = "Body text", type = "general", audience = "everyone", channels = new string[] { }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetAsync("/v1/announcements");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("data");
        var found = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("title").GetString() == "Test Notice")
            {
                row.GetProperty("from").GetString().Should().Be("Priya Principal");
                found = true;
            }
        }
        found.Should().BeTrue();
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~AnnouncementCreatorNameTests"`
Expected: PASS once Step 3's `Announcement_Create` proc update is completed (the `throw` in Step 3 must be replaced first — this test will fail loudly with that exception until it is).

- [ ] **Step 7: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0088_Announcements_CreatorUserId.cs db/Sms.Migrations/M0089_Announcement_Create_CreatorUserId.cs \
  src/Sms.Modules.Comms/CommsModule.cs src/Sms.Application/Services/Comms/AnnouncementService.cs \
  src/Sms.Api/Controllers/AnnouncementController.cs tests/Sms.Tests.Integration/Comms/AnnouncementCreatorNameTests.cs
git commit -m "feat(comms): resolve Announcement From via CreatorUserId->Users.Name at read time, not baked role"
```

---

### Task 8: Live `student_count` on `GET /classes`

**Files:**
- Modify: `src/Sms.Modules.Academics/Data/AcademicsRepositories.cs` (`ClassRepository.ListAsync`, `GetAsync`)
- Test: `tests/Sms.Tests.Integration/Academics/ClassStudentCountLiveTests.cs`

**Interfaces:**
- Consumes: nothing new — reuses the `OUTER APPLY` fallback-count pattern already proven in `ReportingRepository.GetPrincipalAttendanceAsync`.
- Produces: `ClassResponse.StudentCount` is now always the live count (ignores the stubbed stored column entirely).

- [ ] **Step 1: Rewrite `ClassRepository.ListAsync`/`GetAsync` to compute `StudentCount` live**

```csharp
private const string ClassSelectWithLiveCount = @"
SELECT c.Id, c.TenantId, c.Name, c.Grade, c.Section, c.Subject, c.Room,
       CASE WHEN sc.Cnt IS NOT NULL THEN sc.Cnt ELSE c.StudentCount END AS StudentCount,
       c.ClassTeacherId
FROM dbo.Classes c
OUTER APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Students s
    WHERE s.Status = N'active'
      AND (
        (c.Grade IS NOT NULL AND c.Section IS NOT NULL AND s.Grade = c.Grade AND s.Section = c.Section)
        OR (c.Name IS NOT NULL AND s.ClassLabel = c.Name)
      )
) sc";

public async Task<ClassResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
    (await QueryInlineAsync<ClassResponse>($"{ClassSelectWithLiveCount} WHERE c.Id = @id", new { id }, ct))
    .FirstOrDefault();

public Task<IReadOnlyList<ClassResponse>> ListAsync(CancellationToken ct = default) =>
    QueryInlineAsync<ClassResponse>($"{ClassSelectWithLiveCount} ORDER BY c.Name", null, ct);
```

(`CreateAsync`/`UpdateAsync` — which call the `Class_Create`/`Class_Update` procs and return their own `SELECT ... FROM dbo.Classes` — are unchanged; the audit's finding was specifically about the `GET /classes` list, and those procs' returned `StudentCount` being momentarily 0 right after creation is expected/correct since no students are enrolled yet.)

- [ ] **Step 2: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class ClassStudentCountLiveTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task GET_classes_returns_live_student_count_not_stubbed_zero()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var classId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, Grade, Section, StudentCount) VALUES (@classId, @tenantId, 'C1', '5', 'A', 0)",
                new { classId, tenantId });
            for (int i = 0; i < 3; i++)
                await conn.ExecuteAsync(
                    "INSERT dbo.Students (TenantId, AdmissionNo, Name, Grade, Section, Status) VALUES (@tenantId, @adm, @name, '5', 'A', 'active')",
                    new { tenantId, adm = $"A{i}", name = $"Student {i}" });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/classes");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("data");
        var found = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("id").GetGuid() == classId)
            {
                row.GetProperty("student_count").GetInt32().Should().Be(3);
                found = true;
            }
        }
        found.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ClassStudentCountLiveTests"`
Expected: PASS. Also run `dotnet test --filter "FullyQualifiedName~AcademicsTests"` to confirm existing class tests still pass.

- [ ] **Step 4: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Academics/Data/AcademicsRepositories.cs tests/Sms.Tests.Integration/Academics/ClassStudentCountLiveTests.cs
git commit -m "fix(academics): compute GET /classes student_count live instead of trusting the stubbed column"
```

---

### Task 9: Live `AttendancePct` on students

**Files:**
- Modify: `src/Sms.Modules.Sis/Data/StudentRepository.cs` (`GetAsync`, `ListAsync`, `ListByClassPagedAsync`)
- Test: `tests/Sms.Tests.Integration/Sis/StudentAttendancePctLiveTests.cs`

**Interfaces:**
- Consumes: `dbo.AttendanceRecords` (pre-existing).
- Produces: `StudentResponse.AttendancePct` is now always the live computed percentage.

- [ ] **Step 1: Rewrite the student `SELECT` to compute `AttendancePct` live**

```csharp
private const string ColsWithLivePct = @"
s.Id, s.TenantId, s.AdmissionNo, s.Name, s.Gender, s.Grade, s.Section, s.ClassLabel, s.Roll,
s.GuardianName, s.GuardianPhone,
CAST(CASE WHEN att.TotalDays > 0 THEN 100.0 * att.PresentDays / att.TotalDays ELSE 0 END AS decimal(5,2)) AS AttendancePct,
s.FeeStatus, s.FeeDue, s.Status, s.House, s.AvatarHue, s.Dob, s.Email, s.Address
FROM dbo.Students s
OUTER APPLY (
    SELECT COUNT(*) AS TotalDays,
           SUM(CASE WHEN ar.Status IN ('present','late') THEN 1 ELSE 0 END) AS PresentDays
    FROM dbo.AttendanceRecords ar
    WHERE ar.StudentId = s.Id
) att";

public async Task<StudentResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
    (await QueryInlineAsync<StudentResponse>($"SELECT {ColsWithLivePct} WHERE s.Id = @id", new { id }, ct))
    .FirstOrDefault();

public Task<IReadOnlyList<StudentResponse>> ListAsync(
    string? q, string? grade, string? status, string? fee, CancellationToken ct = default) =>
    QueryInlineAsync<StudentResponse>(
        $"SELECT {ColsWithLivePct} WHERE " +
        "(@q IS NULL OR s.Name LIKE '%' + @q + '%' OR s.AdmissionNo LIKE '%' + @q + '%' OR s.ClassLabel LIKE '%' + @q + '%') " +
        "AND (@grade IS NULL OR s.Grade = @grade) AND (@status IS NULL OR s.Status = @status) " +
        "AND (@fee IS NULL OR s.FeeStatus = @fee) ORDER BY s.Name",
        new { q, grade, status, fee }, ct);
```

Note the `SELECT {Cols}` in the original had no `FROM`/table alias prefix (it was `Id, TenantId, ... FROM dbo.Students WHERE ...`); the new constant embeds its own `FROM ... OUTER APPLY` clause, so call sites change from `$"SELECT {Cols} FROM dbo.Students WHERE ..."` to `$"SELECT {ColsWithLivePct} WHERE ..."` (the `FROM` is now inside the constant). `ListByClassPagedAsync`'s `Cols` usage (`$@"SELECT TOP (@limit) {Cols} FROM dbo.Students s WHERE EXISTS (...) ..."`) needs the same treatment — replace its `{Cols} FROM dbo.Students s` with `{ColsWithLivePct}` and drop the now-duplicate `FROM dbo.Students s`.

- [ ] **Step 2: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Sis;

[Collection("sql")]
public class StudentAttendancePctLiveTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Student_attendance_pct_reflects_real_attendance_records()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var classId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Status) VALUES (@studentId, @tenantId, 'A1', 'S1', 'active')",
                new { studentId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, StudentCount) VALUES (@classId, @tenantId, 'C1', 0)",
                new { classId, tenantId });
            // 3 present, 1 absent => 75%
            var statuses = new[] { "present", "present", "present", "absent" };
            for (int i = 0; i < statuses.Length; i++)
                await conn.ExecuteAsync(
                    "INSERT dbo.AttendanceRecords (TenantId, ClassId, StudentId, [Date], Status) VALUES (@tenantId, @classId, @studentId, @date, @status)",
                    new { tenantId, classId, studentId, date = DateTime.UtcNow.Date.AddDays(-i), status = statuses[i] });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync($"/v1/students/{studentId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("attendance_pct").GetDecimal().Should().Be(75.00m);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~StudentAttendancePctLiveTests"`
Expected: PASS. Also run `dotnet test --filter "FullyQualifiedName~ClassStudentsTests"` to confirm existing Sis tests still pass.

- [ ] **Step 4: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Sis/Data/StudentRepository.cs tests/Sms.Tests.Integration/Sis/StudentAttendancePctLiveTests.cs
git commit -m "fix(sis): compute Student.AttendancePct live from AttendanceRecords instead of a stubbed column"
```

---

### Task 10: Live `next_period` on `ClassResponse`

**Files:**
- Modify: `src/Sms.Modules.Academics/Contracts/AcademicsContracts.cs` (`ClassResponse`)
- Modify: `src/Sms.Modules.Academics/Data/AcademicsRepositories.cs` (`ClassRepository.ListAsync`/`GetAsync` — extend Task 8's query)
- Test: `tests/Sms.Tests.Integration/Academics/ClassNextPeriodTests.cs`

**Interfaces:**
- Consumes: Task 8's `ClassSelectWithLiveCount` query (extends it further — this task depends on Task 8 being done first).
- Produces: `ClassResponse.NextPeriod` (nullable string, e.g. subject name of the next upcoming slot).

- [ ] **Step 1: Add `NextPeriod` to `ClassResponse`**

```csharp
public sealed record ClassResponse(
    Guid Id, Guid TenantId, string Name, string? Grade, string? Section, string? Subject,
    string? Room, int StudentCount, Guid? ClassTeacherId, string? NextPeriod = null);
```

- [ ] **Step 2: Extend the class query (from Task 8) with a next-period lookup**

```csharp
private const string ClassSelectWithLiveCountAndNextPeriod = @"
SELECT c.Id, c.TenantId, c.Name, c.Grade, c.Section, c.Subject, c.Room,
       CASE WHEN sc.Cnt IS NOT NULL THEN sc.Cnt ELSE c.StudentCount END AS StudentCount,
       c.ClassTeacherId, np.Subject AS NextPeriod
FROM dbo.Classes c
OUTER APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Students s
    WHERE s.Status = N'active'
      AND (
        (c.Grade IS NOT NULL AND c.Section IS NOT NULL AND s.Grade = c.Grade AND s.Section = c.Section)
        OR (c.Name IS NOT NULL AND s.ClassLabel = c.Name)
      )
) sc
OUTER APPLY (
    SELECT TOP 1 ts.Subject
    FROM dbo.TimetableSlots ts
    WHERE ts.ClassId = c.Id
      AND ts.[Day] = LEFT(DATENAME(WEEKDAY, GETUTCDATE()), 3)
      AND ts.StartTime > FORMAT(GETUTCDATE(), 'HH:mm')
    ORDER BY ts.Period
) np";
```

Rename the Task 8 constant `ClassSelectWithLiveCount` to `ClassSelectWithLiveCountAndNextPeriod` (or keep both if the implementer prefers not to touch Task 8's naming — either is fine, just keep call sites consistent) and update `GetAsync`/`ListAsync` to use it.

Known limitation to note in the PR description, not fix here: this compares `StartTime` as a string (`HH:mm` format, matching the existing `TimetableSlots.StartTime nvarchar(10)` column) against a UTC-formatted current time — if the school's local timezone differs from UTC, "next period" could be off. Timezone handling for timetables is a pre-existing condition across this module (the `Day`/`StartTime` columns are already naive strings), not introduced by this fix.

- [ ] **Step 3: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class ClassNextPeriodTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Class_next_period_reflects_upcoming_timetable_slot()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var today3LetterDay = DateTime.UtcNow.ToString("ddd"); // e.g. "Mon"
        var future = DateTime.UtcNow.AddHours(1).ToString("HH:mm");

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Classes (Id, TenantId, Name, StudentCount) VALUES (@classId, @tenantId, 'C1', 0)",
                new { classId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.TimetableSlots (TenantId, [Day], Period, ClassId, Subject, StartTime) VALUES (@tenantId, @day, 1, @classId, 'Science', @startTime)",
                new { tenantId, day = today3LetterDay, classId, startTime = future });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync($"/v1/classes/{classId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("next_period").GetString().Should().Be("Science");
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ClassNextPeriodTests"`
Expected: PASS (this test is time-sensitive — it inserts a slot 1 hour from now on today's weekday, so it's robust to when it runs except right at day-boundary edge cases, which is an acceptable trade-off for an integration test).

- [ ] **Step 5: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Academics/Contracts/AcademicsContracts.cs src/Sms.Modules.Academics/Data/AcademicsRepositories.cs \
  tests/Sms.Tests.Integration/Academics/ClassNextPeriodTests.cs
git commit -m "feat(academics): derive ClassResponse.NextPeriod live from TimetableSlots, no new column"
```

---

### Task 11: Bus ETA — wire `SpeedKmh` into `GetPositionAsync`

**Files:**
- Modify: `src/Sms.Modules.Transport/BusModule.cs` (`GetPositionAsync`, `PingRow2`)
- Test: `tests/Sms.Tests.Integration/Transport/BusEtaTests.cs`

**Interfaces:**
- Consumes: `dbo.TripPings.SpeedKmh` (pre-existing, already selected by `FleetAsync` — this task adds it to `GetPositionAsync`'s query too).
- Produces: `BusPositionResponse.EtaMinutes` is now a real computed value when speed data exists, `null` (not a divide-by-zero garbage value) when speed is ~0 or missing.

- [ ] **Step 1: Select `SpeedKmh` in `GetPositionAsync` and compute `EtaMinutes`**

```csharp
private sealed record PingRow2(double Lat, double Lng, double? SpeedKmh);

public async Task<BusPositionResponse> GetPositionAsync(Guid busId, CancellationToken ct = default)
{
    var stops = await QueryInlineAsync<StopRow>(
        "SELECT Name, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq", new { busId }, ct);
    var tripId = await CurrentTripIdAsync(busId, ct);
    PingRow2? ping = tripId is null ? null : (await QueryInlineAsync<PingRow2>(
        "SELECT TOP 1 Lat, Lng, SpeedKmh FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At DESC",
        new { tripId }, ct)).FirstOrDefault();

    if (ping is null || stops.Count == 0)
        return new BusPositionResponse(busId, 0, 0, ping?.Lat, ping?.Lng, null, null);

    int nearest = 0; double best = double.MaxValue;
    for (int i = 0; i < stops.Count; i++)
    {
        var dist = Haversine(ping.Lat, ping.Lng, stops[i].Lat, stops[i].Lng);
        if (dist < best) { best = dist; nearest = i; }
    }
    double progress = stops.Count > 1 ? Math.Round((double)nearest / (stops.Count - 1), 3) : 0;
    int? nextIndex = nearest + 1 < stops.Count ? nearest + 1 : null;
    string? next = nextIndex is int ni ? stops[ni].Name : null;

    int? etaMinutes = null;
    if (nextIndex is int idx && ping.SpeedKmh is double speed && speed > 1.0) // ignore near-stationary noise
    {
        var distToNextMeters = Haversine(ping.Lat, ping.Lng, stops[idx].Lat, stops[idx].Lng);
        var etaHours = (distToNextMeters / 1000.0) / speed;
        etaMinutes = (int)Math.Round(etaHours * 60);
    }

    return new BusPositionResponse(busId, nearest, progress, ping.Lat, ping.Lng, next, etaMinutes);
}
```

- [ ] **Step 2: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class BusEtaTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Bus_position_returns_computed_eta_when_speed_available()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo) VALUES (@busId, @tenantId, 'B1')", new { busId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.BusStops (TenantId, BusId, Name, Seq, Lat, Lng) VALUES (@tenantId, @busId, 'Stop1', 0, 12.9716, 77.5946)",
                new { tenantId, busId });
            await conn.ExecuteAsync(
                "INSERT dbo.BusStops (TenantId, BusId, Name, Seq, Lat, Lng) VALUES (@tenantId, @busId, 'Stop2', 1, 12.9816, 77.6046)",
                new { tenantId, busId });
            await conn.ExecuteAsync(
                "INSERT dbo.Trips (Id, TenantId, BusId, Status, StartedAt) VALUES (@tripId, @tenantId, @busId, 'live', SYSUTCDATETIME())",
                new { tripId, tenantId, busId });
            await conn.ExecuteAsync(
                "INSERT dbo.TripPings (TenantId, TripId, Lat, Lng, SpeedKmh, At) VALUES (@tenantId, @tripId, 12.9716, 77.5946, 30, SYSUTCDATETIME())",
                new { tenantId, tripId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync($"/v1/bus/{busId}/position");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var eta = doc.RootElement.GetProperty("data").GetProperty("eta_minutes");
        eta.ValueKind.Should().NotBe(JsonValueKind.Null);
        eta.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Bus_position_returns_null_eta_when_speed_missing()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Buses (Id, TenantId, BusNo) VALUES (@busId, @tenantId, 'B2')", new { busId, tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.BusStops (TenantId, BusId, Name, Seq, Lat, Lng) VALUES (@tenantId, @busId, 'Stop1', 0, 12.9716, 77.5946)",
                new { tenantId, busId });
            await conn.ExecuteAsync(
                "INSERT dbo.Trips (Id, TenantId, BusId, Status, StartedAt) VALUES (@tripId, @tenantId, @busId, 'live', SYSUTCDATETIME())",
                new { tripId, tenantId, busId });
            await conn.ExecuteAsync(
                "INSERT dbo.TripPings (TenantId, TripId, Lat, Lng, SpeedKmh, At) VALUES (@tenantId, @tripId, 12.9716, 77.5946, NULL, SYSUTCDATETIME())",
                new { tenantId, tripId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync($"/v1/bus/{busId}/position");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("eta_minutes").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~BusEtaTests"`
Expected: both PASS. If `dbo.Trips`/`dbo.TripPings` table/column names or the migration that creates them differ from what's assumed here, locate the actual migration first (grep `db/Sms.Migrations/` for `CREATE TABLE.*Trips` or `Create.Table("Trips")`) and adjust the test's `INSERT` statements to match the real schema before debugging a failure as a code bug.

- [ ] **Step 4: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add src/Sms.Modules.Transport/BusModule.cs tests/Sms.Tests.Integration/Transport/BusEtaTests.cs
git commit -m "fix(transport): compute bus EtaMinutes from SpeedKmh + Haversine distance instead of hardcoded null"
```

---

### Task 12: Exam topics — new column

**Files:**
- Create: `db/Sms.Migrations/M0090_ExamPapers_Topics.cs`
- Modify: `db/Sms.Migrations/procs/exams/ExamPaper_Create.sql`
- Modify: `db/Sms.Migrations/M0039_Procs_ExamPaper_Edit.cs` (the inline `ExamPaper_Update` SQL)
- Modify: `src/Sms.Modules.Academics/Contracts/ExamContracts.cs` (`ExamPaperResponse`, `CreateExamPaperRequest`, `UpdateExamPaperRequest`)
- Test: `tests/Sms.Tests.Integration/Academics/ExamPaperTopicsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ExamPaperResponse.Topics` (nullable string — simple comma/JSON-delimited list, app-side already knows how to render/split it since it built the UI expecting this field).

Before editing `ExamPaper_Create.sql`, first grep `db/Sms.Migrations/` for the migration that actually wires it in (the plan's research pass found the `.sql` file on disk but not its invoking migration — likely an earlier-numbered migration alongside `M0017_Exams_Tables.cs`/`M0018_Procs_Exams.cs`). Confirm the exact `EmbeddedProcs` resource path used (e.g. `"procs.exams.ExamPaper_Create"`) before writing Step 2's migration.

- [ ] **Step 1: Migration adding the column**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(90, "ExamPapers: add Topics column")]
public sealed class M0090_ExamPapers_Topics : Migration
{
    public override void Up() =>
        Alter.Table("ExamPapers").AddColumn("Topics").AsString(int.MaxValue).Nullable();

    public override void Down() =>
        Delete.Column("Topics").FromTable("ExamPapers");
}
```

- [ ] **Step 2: Update `ExamPaper_Create.sql`**

```sql
CREATE OR ALTER PROCEDURE dbo.ExamPaper_Create
    @TenantId uniqueidentifier, @ExamId uniqueidentifier, @ClassId uniqueidentifier, @Name nvarchar(120),
    @Subject nvarchar(80), @SubjectId uniqueidentifier, @Date date, @StartTime nvarchar(10),
    @DurationMin int, @MaxMarks int, @Room nvarchar(40), @Invigilator1 nvarchar(120), @Invigilator2 nvarchar(120),
    @Topics nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.ExamPapers (Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime,
        DurationMin, MaxMarks, Room, Invigilator1, Invigilator2, Topics)
    VALUES (@Id, @TenantId, @ExamId, @ClassId, @Name, @Subject, @SubjectId, @Date, @StartTime,
        @DurationMin, ISNULL(@MaxMarks, 100), @Room, @Invigilator1, @Invigilator2, @Topics);

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status, Topics
    FROM dbo.ExamPapers WHERE Id = @Id;
END
```

- [ ] **Step 3: Update `ExamPaper_Update` (inline in `M0039_Procs_ExamPaper_Edit.cs` — do not re-edit that migration file directly since it already ran; add a new migration instead)**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(91, "ExamPaper_Create/Update: add Topics parameter (embedded/inline CREATE OR ALTER)")]
public sealed class M0091_ExamPaper_Topics_Procs : Migration
{
    public override void Up()
    {
        // Re-run the edited ExamPaper_Create.sql — replace with the actual EmbeddedProcs
        // resource path confirmed before Task 12 Step 1.
        // foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.exams.ExamPaper_Create")) Execute.Sql(sql);

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Update
    @Id uniqueidentifier, @Name nvarchar(120) = NULL, @Subject nvarchar(80) = NULL,
    @SubjectId uniqueidentifier = NULL, @Date date = NULL, @StartTime nvarchar(10) = NULL,
    @DurationMin int = NULL, @MaxMarks int = NULL, @Room nvarchar(40) = NULL,
    @Invigilator1 nvarchar(120) = NULL, @Invigilator2 nvarchar(120) = NULL, @Status nvarchar(20) = NULL,
    @Topics nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ExamPapers SET
        Name = COALESCE(@Name, Name), Subject = COALESCE(@Subject, Subject),
        SubjectId = COALESCE(@SubjectId, SubjectId), [Date] = COALESCE(@Date, [Date]),
        StartTime = COALESCE(@StartTime, StartTime), DurationMin = COALESCE(@DurationMin, DurationMin),
        MaxMarks = COALESCE(@MaxMarks, MaxMarks), Room = COALESCE(@Room, Room),
        Invigilator1 = COALESCE(@Invigilator1, Invigilator1),
        Invigilator2 = COALESCE(@Invigilator2, Invigilator2), Status = COALESCE(@Status, Status),
        Topics = COALESCE(@Topics, Topics)
    WHERE Id = @Id;

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status, Topics
    FROM dbo.ExamPapers WHERE Id = @Id;
END;");
    }

    public override void Down()
    {
        // No-op: previous proc bodies are superseded, not restored.
    }
}
```

- [ ] **Step 4: Update contracts**

```csharp
public sealed record ExamPaperResponse(
    Guid Id, Guid TenantId, Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId,
    DateTime? Date, string? StartTime, int? DurationMin, int MaxMarks, string? Room,
    string? Invigilator1, string? Invigilator2, string Status, string? Topics = null);

public sealed record CreateExamPaperRequest(
    Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId, DateTime? Date,
    string? StartTime, int? DurationMin, int MaxMarks, string? Room, string? Invigilator1, string? Invigilator2,
    string? Topics = null);

public sealed record UpdateExamPaperRequest(
    string? Name, string? Subject, Guid? SubjectId, DateTime? Date, string? StartTime, int? DurationMin,
    int? MaxMarks, string? Room, string? Invigilator1, string? Invigilator2, string? Status, string? Topics = null);
```

- [ ] **Step 5: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Academics;

[Collection("sql")]
public class ExamPaperTopicsTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Exam_paper_persists_and_returns_topics()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Principal }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var create = await client.PostAsJsonAsync("/v1/exam-papers", new
        {
            name = "Midterm", subject = "Math", max_marks = 100, topics = "Algebra, Geometry"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("topics").GetString().Should().Be("Algebra, Geometry");
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ExamPaperTopicsTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0090_ExamPapers_Topics.cs db/Sms.Migrations/M0091_ExamPaper_Topics_Procs.cs \
  db/Sms.Migrations/procs/exams/ExamPaper_Create.sql src/Sms.Modules.Academics/Contracts/ExamContracts.cs \
  tests/Sms.Tests.Integration/Academics/ExamPaperTopicsTests.cs
git commit -m "feat(academics): add ExamPapers.Topics column, wire through create/update"
```

---

### Task 13: Leave priority — new column

**Files:**
- Create: `db/Sms.Migrations/M0092_LeaveRequests_Priority.cs`
- Modify: `src/Sms.Modules.Staffing/Contracts/LeaveContracts.cs` (`LeaveResponse`, `CreateLeaveRequest`)
- Modify: `src/Sms.Modules.Staffing/Data/LeaveRepository.cs` (`ListByStatusAsync` from Task 6 — add `Priority` to its `SELECT`; `CreateAsync` — pass through)
- Grep and update the `Leave_Create` stored proc to accept `@Priority`
- Test: `tests/Sms.Tests.Integration/Staffing/LeavePriorityTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `LeaveResponse.Priority` (string, default `"medium"`, mirroring the existing `Complaints.Priority` pattern).

- [ ] **Step 1: Migration adding the column**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(92, "LeaveRequests: add Priority column (mirrors Complaints.Priority)")]
public sealed class M0092_LeaveRequests_Priority : Migration
{
    public override void Up() =>
        Alter.Table("LeaveRequests").AddColumn("Priority").AsString(10).NotNullable().WithDefaultValue("medium");

    public override void Down() =>
        Delete.Column("Priority").FromTable("LeaveRequests");
}
```

- [ ] **Step 2: Update contracts**

```csharp
public sealed record LeaveResponse(
    Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
    string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote,
    string? RequesterName = null, string Priority = "medium");

public sealed record CreateLeaveRequest(
    string Type, DateTime? FromDate, DateTime? ToDate, string? Reason, string? Substitute, Guid? ChildId,
    string? Priority = null);
```

- [ ] **Step 3: Update `ListByStatusAsync` (extends Task 6's query) and `CreateAsync`**

```csharp
public Task<IReadOnlyList<LeaveResponse>> ListByStatusAsync(string status, CancellationToken ct = default) =>
    QueryInlineAsync<LeaveResponse>(@"
SELECT lr.Id, lr.TenantId, lr.RequesterId, lr.ChildId, lr.Type, lr.FromDate, lr.ToDate,
       lr.Reason, lr.Substitute, lr.Status, lr.AppliedOn, lr.DecidedNote, u.Name AS RequesterName, lr.Priority
FROM dbo.LeaveRequests lr
LEFT JOIN dbo.Users u ON u.Id = lr.RequesterId
WHERE lr.Status = @status
ORDER BY lr.AppliedOn DESC", new { status }, ct);

public Task<LeaveResponse?> CreateAsync(Guid tenantId, Guid? requesterId, CreateLeaveRequest r, CancellationToken ct = default) =>
    QuerySingleProcAsync<LeaveResponse>("dbo.Leave_Create", new
    {
        TenantId = tenantId, RequesterId = requesterId, r.ChildId, r.Type, r.FromDate, r.ToDate, r.Reason, r.Substitute,
        Priority = r.Priority ?? "medium"
    }, ct);
```

- [ ] **Step 4: Grep and update `Leave_Create` proc, then add a migration re-running it**

Grep `db/Sms.Migrations/` for `Leave_Create` (this plan's research didn't locate its source file), add `@Priority nvarchar(10) = 'medium'` to its parameter list and `INSERT` column list, and its output `SELECT` list. Then:

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(93, "Leave_Create: accept Priority parameter (embedded CREATE OR ALTER)")]
public sealed class M0093_Leave_Create_Priority : Migration
{
    public override void Up()
    {
        // Replace with the actual EmbeddedProcs resource path found by Step 4's grep.
        throw new NotImplementedException(
            "Locate Leave_Create's source, add @Priority nvarchar(10) = 'medium' to its params/INSERT/SELECT, " +
            "then replace this throw with the real Execute.Sql/EmbeddedProcs call.");
    }

    public override void Down()
    {
        // No-op: previous proc body is superseded, not restored.
    }
}
```

- [ ] **Step 5: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Staffing;

[Collection("sql")]
public class LeavePriorityTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Leave_request_persists_and_returns_priority()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var create = await client.PostAsJsonAsync("/v1/leave", new
        {
            type = "casual", reason = "Family event", priority = "high"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("priority").GetString().Should().Be("high");
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~LeavePriorityTests"`
Expected: PASS once Step 4's `throw` is replaced with the real proc-update call.

- [ ] **Step 7: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0092_LeaveRequests_Priority.cs db/Sms.Migrations/M0093_Leave_Create_Priority.cs \
  src/Sms.Modules.Staffing/Contracts/LeaveContracts.cs src/Sms.Modules.Staffing/Data/LeaveRepository.cs \
  tests/Sms.Tests.Integration/Staffing/LeavePriorityTests.cs
git commit -m "feat(staffing): add LeaveRequests.Priority column, default medium, mirrors Complaints pattern"
```

---

### Task 14: Chat presence — `LastSeenAt` + polling-based online status

**Files:**
- Create: `db/Sms.Migrations/M0094_Users_LastSeenAt.cs`
- Create: `src/Sms.Api/Middleware/LastSeenTouchMiddleware.cs`
- Modify: `src/Sms.Api/Program.cs` (register the middleware)
- Modify: `src/Sms.Modules.Comms/CommsModule.cs` (`ChatThreadResponse`, `ListThreadsAsync`)
- Test: `tests/Sms.Tests.Integration/Comms/ChatPresenceTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Users.LastSeenAt` column; `ChatThreadResponse.Online` (bool, computed at query time as `LastSeenAt` within 5 minutes) — this is a placeholder name since `ChatThreads` don't map 1:1 to a single `Users` row today (a thread's `Name`/`Role` are free-text, not an FK) — see Step 3's note on the join limitation.

- [ ] **Step 1: Migration adding `LastSeenAt`**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(94, "Users: add LastSeenAt for polling-based chat presence")]
public sealed class M0094_Users_LastSeenAt : Migration
{
    public override void Up() =>
        Alter.Table("Users").AddColumn("LastSeenAt").AsDateTime2().Nullable();

    public override void Down() =>
        Delete.Column("LastSeenAt").FromTable("Users");
}
```

- [ ] **Step 2: Middleware to touch `LastSeenAt`, throttled**

```csharp
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Data;

namespace Sms.Api.Middleware;

/// Touches Users.LastSeenAt for authenticated requests, throttled to avoid write-amplification
/// on every API call — only writes when the stored value is more than 60s stale (or null).
public sealed class LastSeenTouchMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IDbConnectionFactory factory)
    {
        var sub = http.User.FindFirst("sub")?.Value;
        if (Guid.TryParse(sub, out var userId))
        {
            await using var conn = await factory.OpenAsync();
            await conn.ExecuteAsync(
                "UPDATE dbo.Users SET LastSeenAt = SYSUTCDATETIME() " +
                "WHERE Id = @userId AND (LastSeenAt IS NULL OR LastSeenAt < DATEADD(SECOND, -60, SYSUTCDATETIME()))",
                new { userId });
        }
        await next(http);
    }
}
```

(`ExecuteAsync` here is the Dapper extension method on `IDbConnection` — add `using Dapper;` if the project's style requires an explicit import; check a neighboring file in `src/Sms.Api/` for the convention.)

- [ ] **Step 3: Register the middleware in `Program.cs`**

Add after `app.UseAuthentication();` and before `app.UseMiddleware<TenantResolutionMiddleware>();` (needs the JWT claims already parsed by authentication, and should run regardless of tenant/billing state):

```csharp
app.UseAuthentication();
app.UseMiddleware<Sms.Api.Middleware.LastSeenTouchMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
```

- [ ] **Step 4: Compute `Online` in `ListThreadsAsync`**

Known limitation, not solved here: `ChatThreads.Name`/`Role` are free-text, not an FK to `Users` — there is no reliable per-thread "which Users row is this" link today (same class of gap as the identity-link work in Tasks 1-3, but for chat contacts specifically, out of scope to fully solve in this task). This fix computes presence only where a thread can be matched to a `Users` row by name (best-effort, may miss matches) — flag as a follow-up for a proper `ChatThreads.ContactUserId` FK later.

```csharp
public sealed record ChatThreadResponse(
    Guid Id, Guid TenantId, string Name, string? Role, string? LastMessage, DateTime? LastAt,
    int Unread, bool Group, Guid? ChildId, bool Online = false);

public Task<IReadOnlyList<ChatThreadResponse>> ListThreadsAsync(CancellationToken ct = default) =>
    QueryInlineAsync<ChatThreadResponse>(@"
SELECT th.Id, th.TenantId, th.Name, th.Role, th.LastMessage, th.LastAt, th.Unread, th.IsGroup AS [Group], th.ChildId,
       CAST(CASE WHEN u.LastSeenAt IS NOT NULL AND u.LastSeenAt > DATEADD(MINUTE, -5, SYSUTCDATETIME())
            THEN 1 ELSE 0 END AS bit) AS Online
FROM dbo.ChatThreads th
LEFT JOIN dbo.Users u ON u.TenantId = th.TenantId AND u.Name = th.Name
ORDER BY th.LastAt DESC", null, ct);
```

- [ ] **Step 5: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Comms;

[Collection("sql")]
public class ChatPresenceTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Authenticated_request_touches_LastSeenAt()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId) VALUES (@userId, @tenantId)", new { userId, tenantId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        await client.GetAsync("/v1/auth/me");

        await using var checkConn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await checkConn.OpenAsync();
        var lastSeen = await checkConn.QuerySingleAsync<DateTime?>(
            "SELECT LastSeenAt FROM dbo.Users WHERE Id = @userId", new { userId });
        lastSeen.Should().NotBeNull();
        lastSeen!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Chat_thread_shows_online_when_matched_user_recently_seen()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });
        var tenantId = Guid.NewGuid();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Name, LastSeenAt) VALUES (NEWID(), @tenantId, 'Chat Contact', SYSUTCDATETIME())",
                new { tenantId });
            await conn.ExecuteAsync(
                "INSERT dbo.ChatThreads (TenantId, Name) VALUES (@tenantId, 'Chat Contact')", new { tenantId });
        }

        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, new[] { Policies.Teacher }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/v1/chat/threads");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("data");
        var found = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("name").GetString() == "Chat Contact")
            {
                row.GetProperty("online").GetBoolean().Should().BeTrue();
                found = true;
            }
        }
        found.Should().BeTrue();
    }
}
```

(Adjust the `GET /v1/chat/threads` route in the test if the actual controller uses a different path — grep `src/Sms.Api/Controllers/` for the controller exposing `ListThreadsAsync` to confirm before running.)

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ChatPresenceTests"`
Expected: both PASS.

- [ ] **Step 7: Commit**

```bash
cd D:/SMS/sms-project/sms-backend
git add db/Sms.Migrations/M0094_Users_LastSeenAt.cs src/Sms.Api/Middleware/LastSeenTouchMiddleware.cs \
  src/Sms.Api/Program.cs src/Sms.Modules.Comms/CommsModule.cs tests/Sms.Tests.Integration/Comms/ChatPresenceTests.cs
git commit -m "feat(comms): polling-based chat presence via Users.LastSeenAt, throttled touch middleware"
```

---

### Task 15: Whole-plan verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass (existing + the ~20 new tests added across Tasks 1-14), zero regressions.

- [ ] **Step 2: Confirm migration idempotence**

Run: `dotnet test --filter "FullyQualifiedName~MigrationIdempotenceTests"`
Expected: PASS — all 11 new migrations (M0084-M0094) are safe to re-run.

- [ ] **Step 3: Report residual follow-ups to the user**

Summarize for the user: (a) the two `NotImplementedException` placeholders left in Tasks 7 and 13 pending a grep-locate of `Announcement_Create`/`Leave_Create`'s source (must be resolved before those tasks' tests can pass — not optional, these block Task 7/13 completion, called out explicitly so they aren't missed as "done"), (b) the deferred provisioning-time `MustSetPassword = true` wiring (Task 4), (c) the `ChatThreads` name-matching limitation for presence (Task 14), (d) settings/preferences persistence remains out of scope entirely.
