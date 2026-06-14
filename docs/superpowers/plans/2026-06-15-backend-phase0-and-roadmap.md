# SMS Backend — Phase 0 Foundation Implementation Plan (+ Phase 1–6 Roadmap)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 0 platform foundation of the SMS .NET 10 backend — a runnable, multi-tenant ASP.NET Core Web API with Dapper + SQL Server, stored-procedure data access (where it adds value), FluentMigrator migrations (DDL + procs), JWT auth, RBAC, tenant resolution with Row-Level Security, snake_case/error/paging conventions, observability, Swagger, Docker, and CI — with **no business endpoints yet**.

**Architecture:** Modular monolith (`Sms.Api` host + `Sms.Shared.Kernel` + empty `Sms.Modules.*` placeholders), Minimal APIs grouped by `MapGroup("/v1/...")`. Data access through a Dapper-based base repository that calls stored procedures for writes/complex reads and parameterised inline SQL for simple reads. Multi-tenancy enforced in depth: `SESSION_CONTEXT` set on connection open + SQL Server Row-Level Security on tenant-scoped tables. Migrations (tables, indexes, RLS policies, procedures) run via FluentMigrator on startup in dev.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, Dapper, Microsoft.Data.SqlClient, FluentMigrator, FluentValidation, JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`), Serilog, OpenTelemetry, Swashbuckle (OpenAPI), xUnit + Testcontainers (SQL Server) + FluentAssertions. SQL Server dev instance: **`DESKTOP-TJL4SG6`**.

**Spec:** `docs/superpowers/specs/2026-06-15-backend-stored-procedures-design.md` (this plan implements §1, §3, and Phase 0 of §4). Master design + canonical schema: `docs/2026-06-13-backend-api-design.md`.

**Conventions used throughout:**
- Commands run from repo root `D:\SMS\sms-project\sms-backend` (shown as `/d/SMS/sms-project/sms-backend` in Git Bash).
- Test runner: `dotnet test`. Build: `dotnet build`. Unit tests do NOT need a database.
- **Integration tests run against the real dev SQL Server `DESKTOP-TJL4SG6` (Windows auth).** Docker is NOT installed on this machine, so Testcontainers is not used. The integration fixture creates a uniquely-named throwaway database (`Sms_Test_{guid}`), runs migrations into it, runs the tests, and drops it on teardown. SQL Server 2019 Developer Edition — `SESSION_CONTEXT` + Row-Level Security fully supported.
- Every code-producing task is TDD: failing test → run-fail → implement → run-pass → commit.
- This repo is NOT yet a git repo — **Task 1 Step 0 runs `git init`**.

---

## File Structure (Phase 0)

```
sms-backend/
  Sms.sln
  Directory.Build.props                     # shared TargetFramework, Nullable, LangVersion
  docker-compose.yml                        # API + SQL Server
  .dockerignore
  .gitignore
  src/
    Sms.Api/
      Sms.Api.csproj
      Program.cs                            # DI composition + middleware order + endpoint mapping
      appsettings.json                      # connection string, JWT, logging
      appsettings.Development.json
      Endpoints/
        HealthEndpoints.cs                  # /health, /health/ready
        AuthEndpoints.cs                    # /v1/auth/login|refresh|me|logout
      Auth/
        CurrentUserAccessor.cs              # reads ClaimsPrincipal → ITenantContext population
    Sms.Shared.Kernel/
      Sms.Shared.Kernel.csproj
      Results/Result.cs                     # Result<T>, Error
      Http/ErrorEnvelope.cs                 # { error: { code, message, details } } + { data }
      Http/SnakeCaseNamingPolicy.cs         # JSON snake_case policy
      Http/Paging.cs                        # cursor paging types
      Data/IDbConnectionFactory.cs
      Data/SqlConnectionFactory.cs          # opens SqlConnection + sets SESSION_CONTEXT
      Data/BaseRepository.cs                # proc-call + inline-read Dapper helpers
      Data/DapperSnakeCaseConfig.cs         # MatchNamesWithUnderscores
      Tenancy/ITenantContext.cs
      Tenancy/TenantContext.cs              # mutable per-request scope
      Tenancy/TenantResolutionMiddleware.cs
      Auth/IJwtTokenService.cs
      Auth/JwtTokenService.cs               # issue access + refresh
      Auth/IPasswordHasher.cs
      Auth/PasswordHasher.cs                # PBKDF2
      Auth/IRefreshTokenStore.cs
      Auth/RefreshTokenStore.cs             # hashed, revocable (proc-backed)
      Auth/IOtpSender.cs
      Auth/ConsoleOtpSender.cs              # stub
      Authz/Policies.cs                     # RBAC policy names + registration
      Authz/RequireFeatureAttribute.cs      # tier-gating endpoint filter
      Time/IClock.cs
      Time/SystemClock.cs
    Sms.Modules.Identity/                   # placeholder csproj (filled Phase 0 auth lives in Kernel+Api)
    Sms.Modules.Tenancy/                    # placeholder (Phase 1)
    Sms.Modules.Sis/                        # placeholder (Phase 2)
    Sms.Modules.Staffing/                   # placeholder (Phase 2)
    Sms.Modules.Academics/                  # placeholder (Phase 2)
    Sms.Modules.Attendance/                 # placeholder (Phase 3)
    Sms.Modules.Finance/                    # placeholder (Phase 2/5)
    Sms.Modules.Transport/                  # placeholder (Phase 4)
    Sms.Modules.Comms/                      # placeholder (Phase 3)
    Sms.Modules.Reporting/                  # placeholder (Phase 1/6)
  db/
    Sms.Migrations/
      Sms.Migrations.csproj
      MigrationRunner.cs                    # FluentMigrator startup runner
      M0001_Foundation_Tables.cs           # Tenants, Users, UserRoles, RefreshTokens, OtpCodes, AuditLog
      M0002_Rls_Policies.cs                # SESSION_CONTEXT predicate + security policies
      M0003_Procs_Auth.cs                  # embeds procs/auth/*.sql via CREATE OR ALTER
      procs/
        auth/User_GetByEmail.sql
        auth/User_GetByStudentId.sql
        auth/RefreshToken_Insert.sql
        auth/RefreshToken_GetActive.sql
        auth/RefreshToken_Revoke.sql
        auth/Otp_Insert.sql
        auth/Otp_GetActive.sql
        audit/AuditLog_Insert.sql
  tests/
    Sms.Tests.Unit/
      Sms.Tests.Unit.csproj
      Results/ResultTests.cs
      Http/SnakeCaseNamingPolicyTests.cs
      Auth/PasswordHasherTests.cs
      Auth/JwtTokenServiceTests.cs
      Tenancy/TenantResolutionMiddlewareTests.cs
    Sms.Tests.Integration/
      Sms.Tests.Integration.csproj
      SqlServerFixture.cs                   # Testcontainers SQL Server + migrations
      Data/SessionContextTests.cs
      Data/RlsIsolationTests.cs            # tenant A cannot read tenant B
      Auth/AuthFlowTests.cs                # login → me → refresh → logout
      Health/HealthEndpointTests.cs
```

**Responsibility split:** `Sms.Shared.Kernel` owns all cross-cutting primitives (no business logic, no HTTP host). `Sms.Api` is the only executable — it wires DI + middleware and maps endpoints. `db/Sms.Migrations` owns schema + procs as versioned migrations. Modules are empty class-library placeholders in Phase 0 so the solution graph and `MapGroup` registration points exist; they fill up in later phases.

---

## Task 1: Repo init + solution skeleton + smoke build

**Files:**
- Create: `Sms.sln`, `Directory.Build.props`, `.gitignore`, `src/Sms.Api/Sms.Api.csproj`, `src/Sms.Api/Program.cs`, `src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj`, `tests/Sms.Tests.Unit/Sms.Tests.Unit.csproj`

- [ ] **Step 0: Init git** (repo is not yet under version control)

Run:
```bash
cd /d/SMS/sms-project/sms-backend
git init
printf 'bin/\nobj/\n*.user\n.vs/\nTestResults/\n' > .gitignore
git add .gitignore docs && git commit -m "chore: init sms-backend repo with design docs"
```

- [ ] **Step 1: Create solution + shared props**

Run:
```bash
dotnet new sln -n Sms
```

Create `Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the three starter projects + reference graph**

Run:
```bash
dotnet new classlib -n Sms.Shared.Kernel -o src/Sms.Shared.Kernel
dotnet new web      -n Sms.Api           -o src/Sms.Api
dotnet new xunit    -n Sms.Tests.Unit    -o tests/Sms.Tests.Unit
dotnet sln add src/Sms.Shared.Kernel src/Sms.Api tests/Sms.Tests.Unit
dotnet add src/Sms.Api reference src/Sms.Shared.Kernel
dotnet add tests/Sms.Tests.Unit reference src/Sms.Shared.Kernel
dotnet add tests/Sms.Tests.Unit package FluentAssertions
```

- [ ] **Step 3: Minimal Program.cs (compiles, no endpoints yet)**

Replace `src/Sms.Api/Program.cs`:
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "SMS API");

app.Run();

public partial class Program { } // for WebApplicationFactory in integration tests
```

- [ ] **Step 4: Build to verify the graph compiles**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Sms.sln Directory.Build.props src tests
git commit -m "chore: solution skeleton (Api + Kernel + unit tests)"
```

---

## Task 2: Result<T> + Error primitives

**Files:**
- Create: `src/Sms.Shared.Kernel/Results/Result.cs`
- Test: `tests/Sms.Tests.Unit/Results/ResultTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Results/ResultTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Results;
using Xunit;

namespace Sms.Tests.Unit.Results;

public class ResultTests
{
    [Fact]
    public void Ok_carries_value_and_is_success()
    {
        var r = Result<int>.Ok(42);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_carries_error_and_is_not_success()
    {
        var r = Result<int>.Fail(new Error("not_found", "missing"));
        r.IsSuccess.Should().BeFalse();
        r.Error!.Code.Should().Be("not_found");
        r.Error.Message.Should().Be("missing");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter ResultTests`
Expected: FAIL — `Sms.Shared.Kernel.Results` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Sms.Shared.Kernel/Results/Result.cs`:
```csharp
namespace Sms.Shared.Kernel.Results;

public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    private Result(bool ok, T? value, Error? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(Error error) => new(false, default, error);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter ResultTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Results tests/Sms.Tests.Unit/Results
git commit -m "feat(kernel): Result<T> + Error primitive"
```

---

## Task 3: snake_case JSON policy + error/data envelopes + paging types

**Files:**
- Create: `src/Sms.Shared.Kernel/Http/SnakeCaseNamingPolicy.cs`, `src/Sms.Shared.Kernel/Http/ErrorEnvelope.cs`, `src/Sms.Shared.Kernel/Http/Paging.cs`
- Test: `tests/Sms.Tests.Unit/Http/SnakeCaseNamingPolicyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Http/SnakeCaseNamingPolicyTests.cs`:
```csharp
using System.Text.Json;
using FluentAssertions;
using Sms.Shared.Kernel.Http;
using Xunit;

namespace Sms.Tests.Unit.Http;

public class SnakeCaseNamingPolicyTests
{
    private static JsonSerializerOptions Opts() =>
        new() { PropertyNamingPolicy = new SnakeCaseNamingPolicy() };

    private sealed record Sample(string AdmissionNo, int AvatarHue);

    [Fact]
    public void Serializes_pascal_properties_as_snake_case()
    {
        var json = JsonSerializer.Serialize(new Sample("ADM-1", 210), Opts());
        json.Should().Contain("\"admission_no\":\"ADM-1\"");
        json.Should().Contain("\"avatar_hue\":210");
    }

    [Theory]
    [InlineData("ID", "id")]
    [InlineData("ClassLabel", "class_label")]
    [InlineData("HTTPStatus", "http_status")]
    public void Converts_names(string input, string expected) =>
        new SnakeCaseNamingPolicy().ConvertName(input).Should().Be(expected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter SnakeCaseNamingPolicyTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Http/SnakeCaseNamingPolicy.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace Sms.Shared.Kernel.Http;

public sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prevLower = i > 0 && char.IsLower(name[i - 1]);
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && (prevLower || nextLower)) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
```

Create `src/Sms.Shared.Kernel/Http/ErrorEnvelope.cs`:
```csharp
using Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Http;

public sealed record DataEnvelope<T>(T Data);
public sealed record CursorPage<T>(IReadOnlyList<T> Data, string? NextCursor);
public sealed record ErrorBody(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details);
public sealed record ErrorEnvelope(ErrorBody Error)
{
    public static ErrorEnvelope From(Error e) => new(new ErrorBody(e.Code, e.Message, e.Details));
}
```

Create `src/Sms.Shared.Kernel/Http/Paging.cs`:
```csharp
namespace Sms.Shared.Kernel.Http;

public sealed record PageRequest(int Limit = 50, string? Cursor = null)
{
    public int SafeLimit => Limit is < 1 or > 200 ? 50 : Limit;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter SnakeCaseNamingPolicyTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Http tests/Sms.Tests.Unit/Http
git commit -m "feat(kernel): snake_case JSON policy + data/error envelopes + paging"
```

---

## Task 4: Clock + ITenantContext

**Files:**
- Create: `src/Sms.Shared.Kernel/Time/IClock.cs`, `src/Sms.Shared.Kernel/Time/SystemClock.cs`, `src/Sms.Shared.Kernel/Tenancy/ITenantContext.cs`, `src/Sms.Shared.Kernel/Tenancy/TenantContext.cs`
- Test: `tests/Sms.Tests.Unit/Tenancy/TenantContextTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Tenancy/TenantContextTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void Holds_tenant_and_user_and_platform_flag()
    {
        var ctx = new TenantContext();
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ctx.Set(tid, uid, isPlatform: true);
        ctx.TenantId.Should().Be(tid);
        ctx.UserId.Should().Be(uid);
        ctx.IsPlatform.Should().BeTrue();
    }

    [Fact]
    public void Unset_context_has_null_ids()
    {
        var ctx = new TenantContext();
        ctx.TenantId.Should().BeNull();
        ctx.UserId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter TenantContextTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Time/IClock.cs`:
```csharp
namespace Sms.Shared.Kernel.Time;
public interface IClock { DateTime UtcNow { get; } }
```

Create `src/Sms.Shared.Kernel/Time/SystemClock.cs`:
```csharp
namespace Sms.Shared.Kernel.Time;
public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
```

Create `src/Sms.Shared.Kernel/Tenancy/ITenantContext.cs`:
```csharp
namespace Sms.Shared.Kernel.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatform { get; }
    void Set(Guid? tenantId, Guid? userId, bool isPlatform);
}
```

Create `src/Sms.Shared.Kernel/Tenancy/TenantContext.cs`:
```csharp
namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsPlatform { get; private set; }

    public void Set(Guid? tenantId, Guid? userId, bool isPlatform)
    {
        TenantId = tenantId;
        UserId = userId;
        IsPlatform = isPlatform;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter TenantContextTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Time src/Sms.Shared.Kernel/Tenancy tests/Sms.Tests.Unit/Tenancy
git commit -m "feat(kernel): IClock + ITenantContext (scoped, platform flag)"
```

---

## Task 5: IDbConnectionFactory + SqlConnectionFactory (sets SESSION_CONTEXT)

**Files:**
- Create: `src/Sms.Shared.Kernel/Data/IDbConnectionFactory.cs`, `src/Sms.Shared.Kernel/Data/SqlConnectionFactory.cs`, `src/Sms.Shared.Kernel/Data/DapperSnakeCaseConfig.cs`
- Modify: `src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj` (add Dapper + SqlClient packages)
- Integration test added in Task 12 (needs a real DB).

- [ ] **Step 1: Add data packages**

Run:
```bash
dotnet add src/Sms.Shared.Kernel package Dapper
dotnet add src/Sms.Shared.Kernel package Microsoft.Data.SqlClient
```

- [ ] **Step 2: Write the factory interface**

Create `src/Sms.Shared.Kernel/Data/IDbConnectionFactory.cs`:
```csharp
using System.Data.Common;

namespace Sms.Shared.Kernel.Data;

public interface IDbConnectionFactory
{
    /// Opens a connection and, when a tenant/user is in context, stamps SESSION_CONTEXT
    /// ('TenantId','UserId','IsPlatform') so RLS predicates and procs see the caller.
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Implement the factory**

Create `src/Sms.Shared.Kernel/Data/SqlConnectionFactory.cs`:
```csharp
using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Shared.Kernel.Data;

public sealed class SqlConnectionFactory(string connectionString, ITenantContext tenant) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await StampSessionContextAsync(conn);
        return conn;
    }

    private async Task StampSessionContextAsync(SqlConnection conn)
    {
        if (tenant.TenantId is { } tid)
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@v",
                new { v = tid });
        if (tenant.UserId is { } uid)
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'UserId', @value=@v",
                new { v = uid });
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=@v",
            new { v = tenant.IsPlatform ? 1 : 0 });
    }
}
```

Create `src/Sms.Shared.Kernel/Data/DapperSnakeCaseConfig.cs`:
```csharp
using Dapper;

namespace Sms.Shared.Kernel.Data;

public static class DapperSnakeCaseConfig
{
    private static bool _applied;
    public static void Apply()
    {
        if (_applied) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true; // maps admission_no -> AdmissionNo
        _applied = true;
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/Sms.Shared.Kernel`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Data src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj
git commit -m "feat(kernel): IDbConnectionFactory + SqlConnectionFactory (SESSION_CONTEXT) + Dapper snake_case"
```

---

## Task 6: BaseRepository (proc-call + inline-read Dapper helpers)

**Files:**
- Create: `src/Sms.Shared.Kernel/Data/BaseRepository.cs`
- Test: `tests/Sms.Tests.Unit/Data/BaseRepositoryShapeTests.cs` (compile-time contract test — DB behavior covered in integration Task 12)

- [ ] **Step 1: Write the failing test (contract: helper method names/signatures exist)**

Create `tests/Sms.Tests.Unit/Data/BaseRepositoryShapeTests.cs`:
```csharp
using System.Reflection;
using FluentAssertions;
using Sms.Shared.Kernel.Data;
using Xunit;

namespace Sms.Tests.Unit.Data;

public class BaseRepositoryShapeTests
{
    [Theory]
    [InlineData("QueryProcAsync")]
    [InlineData("QuerySingleProcAsync")]
    [InlineData("ExecuteProcAsync")]
    [InlineData("QueryInlineAsync")]
    public void Exposes_data_helpers(string method)
    {
        typeof(BaseRepository)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .Should().Contain(method);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter BaseRepositoryShapeTests`
Expected: FAIL — `BaseRepository` missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Data/BaseRepository.cs`:
```csharp
using System.Data;
using Dapper;

namespace Sms.Shared.Kernel.Data;

/// Base for all repositories. Stored procedures for writes/complex reads;
/// QueryInlineAsync for simple single-table reads (parameterised only — never string-concat).
public abstract class BaseRepository(IDbConnectionFactory factory)
{
    protected IDbConnectionFactory Factory { get; } = factory;

    protected async Task<IReadOnlyList<T>> QueryProcAsync<T>(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.AsList();
    }

    protected async Task<T?> QuerySingleProcAsync<T>(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    protected async Task<int> ExecuteProcAsync(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        return await conn.ExecuteAsync(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    protected async Task<IReadOnlyList<T>> QueryInlineAsync<T>(
        string sql, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(sql, args, commandType: CommandType.Text, cancellationToken: ct));
        return rows.AsList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter BaseRepositoryShapeTests`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Data/BaseRepository.cs tests/Sms.Tests.Unit/Data
git commit -m "feat(kernel): BaseRepository (proc + inline-read Dapper helpers)"
```

---

## Task 7: PasswordHasher (PBKDF2)

**Files:**
- Create: `src/Sms.Shared.Kernel/Auth/IPasswordHasher.cs`, `src/Sms.Shared.Kernel/Auth/PasswordHasher.cs`
- Test: `tests/Sms.Tests.Unit/Auth/PasswordHasherTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Auth/PasswordHasherTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _h = new PasswordHasher();

    [Fact]
    public void Verify_succeeds_for_correct_password()
    {
        var hash = _h.Hash("Secret123!");
        _h.Verify("Secret123!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = _h.Hash("Secret123!");
        _h.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_differ()
    {
        _h.Hash("same").Should().NotBe(_h.Hash("same"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter PasswordHasherTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Auth/IPasswordHasher.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encoded);
}
```

Create `src/Sms.Shared.Kernel/Auth/PasswordHasher.cs`:
```csharp
using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16, KeySize = 32, Iterations = 100_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('.', 3);
        if (parts.Length != 3) return false;
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter PasswordHasherTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth tests/Sms.Tests.Unit/Auth/PasswordHasherTests.cs
git commit -m "feat(kernel): PBKDF2 password hasher"
```

---

## Task 8: JwtTokenService (access + refresh)

**Files:**
- Create: `src/Sms.Shared.Kernel/Auth/IJwtTokenService.cs`, `src/Sms.Shared.Kernel/Auth/JwtTokenService.cs`, `src/Sms.Shared.Kernel/Auth/JwtOptions.cs`
- Modify: `src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj` (add `System.IdentityModel.Tokens.Jwt`)
- Test: `tests/Sms.Tests.Unit/Auth/JwtTokenServiceTests.cs`

- [ ] **Step 1: Add package**

Run: `dotnet add src/Sms.Shared.Kernel package System.IdentityModel.Tokens.Jwt`

- [ ] **Step 2: Write the failing test**

Create `tests/Sms.Tests.Unit/Auth/JwtTokenServiceTests.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService Service() => new(
        new JwtOptions
        {
            Issuer = "sms",
            Audience = "sms-apps",
            SigningKey = "test-signing-key-at-least-32-bytes-long!!",
            AccessTokenMinutes = 15
        },
        new SystemClock());

    [Fact]
    public void Issues_access_token_with_sub_tenant_role_claims()
    {
        var svc = Service();
        var token = svc.IssueAccess(
            userId: Guid.NewGuid(), tenantId: Guid.NewGuid(),
            roles: new[] { "school.admin" }, isPlatform: false);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "sub");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "school.admin");
    }

    [Fact]
    public void Issues_opaque_refresh_token_that_is_unique()
    {
        var svc = Service();
        svc.NewRefreshToken().Should().NotBe(svc.NewRefreshToken());
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter JwtTokenServiceTests`
Expected: FAIL — types missing.

- [ ] **Step 4: Write implementation**

Create `src/Sms.Shared.Kernel/Auth/JwtOptions.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "sms";
    public string Audience { get; init; } = "sms-apps";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 15;
}
```

Create `src/Sms.Shared.Kernel/Auth/IJwtTokenService.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public interface IJwtTokenService
{
    string IssueAccess(Guid userId, Guid? tenantId, IEnumerable<string> roles, bool isPlatform);
    string NewRefreshToken();
}
```

Create `src/Sms.Shared.Kernel/Auth/JwtTokenService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Sms.Shared.Kernel.Time;

namespace Sms.Shared.Kernel.Auth;

public sealed class JwtTokenService(JwtOptions options, IClock clock) : IJwtTokenService
{
    public string IssueAccess(Guid userId, Guid? tenantId, IEnumerable<string> roles, bool isPlatform)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("is_platform", isPlatform ? "1" : "0"),
        };
        if (tenantId is { } tid) claims.Add(new Claim("tenant_id", tid.ToString()));
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = clock.UtcNow;
        var token = new JwtSecurityToken(
            issuer: options.Issuer, audience: options.Audience, claims: claims,
            notBefore: now, expires: now.AddMinutes(options.AccessTokenMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter JwtTokenServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth tests/Sms.Tests.Unit/Auth/JwtTokenServiceTests.cs src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj
git commit -m "feat(kernel): JWT token service (access claims + opaque refresh)"
```

---

## Task 9: Foundation schema migration (tables) via FluentMigrator

**Files:**
- Create: `db/Sms.Migrations/Sms.Migrations.csproj`, `db/Sms.Migrations/M0001_Foundation_Tables.cs`, `db/Sms.Migrations/MigrationRunner.cs`
- Modify: `Sms.sln` (add project)
- Test: covered by integration Task 12 (real DB). Build-only here.

- [ ] **Step 1: Create migrations project + packages**

Run:
```bash
dotnet new classlib -n Sms.Migrations -o db/Sms.Migrations
dotnet sln add db/Sms.Migrations
dotnet add db/Sms.Migrations package FluentMigrator
dotnet add db/Sms.Migrations package FluentMigrator.Runner
dotnet add db/Sms.Migrations package FluentMigrator.Runner.SqlServer
dotnet add src/Sms.Api reference db/Sms.Migrations
```

- [ ] **Step 2: Write the foundation tables migration**

Create `db/Sms.Migrations/M0001_Foundation_Tables.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(1, "Foundation tables: tenants, users, roles, refresh tokens, otp, audit")]
public sealed class M0001_Foundation_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Tenants")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Slug").AsString(100).NotNullable().Unique()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("trial")
            .WithColumn("Tier").AsString(20).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Table("Users")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().Nullable() // null = platform (Catre) user
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("StudentId").AsString(64).Nullable()
            .WithColumn("Phone").AsString(32).Nullable()
            .WithColumn("PasswordHash").AsString(512).Nullable()
            .WithColumn("IsPlatform").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Users_Tenant_Email").OnTable("Users")
            .OnColumn("TenantId").Ascending().OnColumn("Email").Ascending();

        Create.Table("UserRoles")
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Role").AsString(64).NotNullable();
        Create.PrimaryKey("PK_UserRoles").OnTable("UserRoles").Columns("UserId", "Role");

        Create.Table("RefreshTokens")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("TokenHash").AsString(128).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("RevokedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_RefreshTokens_Hash").OnTable("RefreshTokens").OnColumn("TokenHash").Ascending();

        Create.Table("OtpCodes")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Phone").AsString(32).NotNullable()
            .WithColumn("CodeHash").AsString(128).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("ConsumedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Table("AuditLog")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("ActorId").AsGuid().Nullable()
            .WithColumn("Action").AsString(128).NotNullable()
            .WithColumn("Target").AsString(256).Nullable()
            .WithColumn("Kind").AsString(64).Nullable()
            .WithColumn("At").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down()
    {
        Delete.Table("AuditLog");
        Delete.Table("OtpCodes");
        Delete.Table("RefreshTokens");
        Delete.Table("UserRoles");
        Delete.Table("Users");
        Delete.Table("Tenants");
    }
}
```

- [ ] **Step 3: Write the runner**

Create `db/Sms.Migrations/MigrationRunner.cs`:
```csharp
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Sms.Migrations;

public static class MigrationRunner
{
    public static void Run(string connectionString)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add db/Sms.Migrations Sms.sln src/Sms.Api/Sms.Api.csproj
git commit -m "feat(db): FluentMigrator runner + foundation tables migration"
```

---

## Task 10: RLS policies migration

**Files:**
- Create: `db/Sms.Migrations/M0002_Rls_Policies.cs`
- Test: integration Task 12 proves isolation.

- [ ] **Step 1: Write the RLS migration**

Create `db/Sms.Migrations/M0002_Rls_Policies.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(2, "Row-Level Security: tenant predicate + security policy on tenant-scoped tables")]
public sealed class M0002_Rls_Policies : Migration
{
    public override void Up()
    {
        // Predicate: row visible if it belongs to the SESSION_CONTEXT tenant, OR caller is platform.
        Execute.Sql(@"
CREATE SCHEMA rls;
");
        Execute.Sql(@"
CREATE FUNCTION rls.fn_tenant_predicate(@TenantId uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS allowed
WHERE
    CAST(SESSION_CONTEXT(N'IsPlatform') AS int) = 1
    OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);
");
        // Apply to Users (TenantId nullable: platform users have NULL tenant and are reachable only
        // by platform sessions — predicate returns no row for NULL under a tenant session, which is intended).
        Execute.Sql(@"
CREATE SECURITY POLICY rls.UsersTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Users,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Users AFTER INSERT
WITH (STATE = ON);
");
        Execute.Sql(@"
CREATE SECURITY POLICY rls.AuditTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLog
WITH (STATE = ON);
");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AuditTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.UsersTenantPolicy;");
        Execute.Sql("DROP FUNCTION IF EXISTS rls.fn_tenant_predicate;");
        Execute.Sql("DROP SCHEMA IF EXISTS rls;");
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add db/Sms.Migrations/M0002_Rls_Policies.cs
git commit -m "feat(db): row-level security predicate + policies (tenant isolation + platform bypass)"
```

---

## Task 11: Auth stored procedures migration (embedded .sql via CREATE OR ALTER)

**Files:**
- Create: `db/Sms.Migrations/procs/auth/User_GetByEmail.sql`, `db/Sms.Migrations/procs/auth/RefreshToken_Insert.sql`, `db/Sms.Migrations/procs/auth/RefreshToken_GetActive.sql`, `db/Sms.Migrations/procs/auth/RefreshToken_Revoke.sql`, `db/Sms.Migrations/M0003_Procs_Auth.cs`
- Modify: `db/Sms.Migrations/Sms.Migrations.csproj` (embed `procs/**/*.sql`)
- Test: integration Task 12.

- [ ] **Step 1: Embed proc SQL as resources** — add to `db/Sms.Migrations/Sms.Migrations.csproj` inside `<Project>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="procs/**/*.sql" />
  </ItemGroup>
```

- [ ] **Step 2: Write the proc SQL files**

Create `db/Sms.Migrations/procs/auth/User_GetByEmail.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.User_GetByEmail
    @Email nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.Email = @Email;
END
```

Create `db/Sms.Migrations/procs/auth/RefreshToken_Insert.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.RefreshToken_Insert
    @UserId uniqueidentifier,
    @TokenHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.RefreshTokens (UserId, TokenHash, ExpiresAt)
    VALUES (@UserId, @TokenHash, @ExpiresAt);
END
```

Create `db/Sms.Migrations/procs/auth/RefreshToken_GetActive.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.RefreshToken_GetActive
    @TokenHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rt.Id, rt.UserId, rt.ExpiresAt
    FROM dbo.RefreshTokens rt
    WHERE rt.TokenHash = @TokenHash
      AND rt.RevokedAt IS NULL
      AND rt.ExpiresAt > SYSUTCDATETIME();
END
```

Create `db/Sms.Migrations/procs/auth/RefreshToken_Revoke.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.RefreshToken_Revoke
    @TokenHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RefreshTokens
    SET RevokedAt = SYSUTCDATETIME()
    WHERE TokenHash = @TokenHash AND RevokedAt IS NULL;
END
```

- [ ] **Step 3: Write the migration that applies every embedded proc**

Create `db/Sms.Migrations/M0003_Procs_Auth.cs`:
```csharp
using System.Reflection;
using FluentMigrator;

namespace Sms.Migrations;

[Migration(3, "Auth stored procedures (idempotent CREATE OR ALTER from embedded .sql)")]
public sealed class M0003_Procs_Auth : Migration
{
    public override void Up()
    {
        foreach (var sql in EmbeddedProcs("procs.auth."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_GetByEmail;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_Insert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_GetActive;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_Revoke;");
    }

    internal static IEnumerable<string> EmbeddedProcs(string namespaceFragment)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.Contains(namespaceFragment) && n.EndsWith(".sql"))
                     .OrderBy(n => n))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
```

> Note: embedded resource names use `.` separators (e.g. `Sms.Migrations.procs.auth.User_GetByEmail.sql`), so the fragment `procs.auth.` matches all auth procs.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add db/Sms.Migrations
git commit -m "feat(db): auth stored procedures via embedded CREATE OR ALTER migration"
```

---

## Task 12: Integration test harness — real SQL Server (DESKTOP-TJL4SG6) throwaway DB + migrate + SESSION_CONTEXT + RLS isolation

**Files:**
- Create: `tests/Sms.Tests.Integration/Sms.Tests.Integration.csproj`, `tests/Sms.Tests.Integration/SqlServerFixture.cs`, `tests/Sms.Tests.Integration/Data/SessionContextTests.cs`, `tests/Sms.Tests.Integration/Data/RlsIsolationTests.cs`
- Modify: `Sms.sln`
- **Requires SQL Server `DESKTOP-TJL4SG6` reachable via Windows auth** (no Docker on this machine). Override the server with env var `SMS_TEST_SQL_SERVER` if different.

- [ ] **Step 1: Create the project + packages + references**

Run:
```bash
dotnet new xunit -n Sms.Tests.Integration -o tests/Sms.Tests.Integration
dotnet sln add tests/Sms.Tests.Integration
dotnet add tests/Sms.Tests.Integration reference src/Sms.Shared.Kernel db/Sms.Migrations
dotnet add tests/Sms.Tests.Integration package Dapper
dotnet add tests/Sms.Tests.Integration package Microsoft.Data.SqlClient
dotnet add tests/Sms.Tests.Integration package FluentAssertions
```

- [ ] **Step 2: Write the fixture (creates a throwaway DB on the host SQL Server, migrates it, drops it on teardown)**

Create `tests/Sms.Tests.Integration/SqlServerFixture.cs`:
```csharp
using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Migrations;
using Xunit;

namespace Sms.Tests.Integration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string _server =
        Environment.GetEnvironmentVariable("SMS_TEST_SQL_SERVER") ?? "DESKTOP-TJL4SG6";
    private readonly string _dbName = "Sms_Test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    private string MasterCs =>
        $"Server={_server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    public async Task InitializeAsync()
    {
        await using (var master = new SqlConnection(MasterCs))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{_dbName}];");
        }
        ConnectionString =
            $"Server={_server};Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
        MigrationRunner.Run(ConnectionString); // tables + RLS + procs
    }

    public async Task DisposeAsync()
    {
        await using var master = new SqlConnection(MasterCs);
        await master.OpenAsync();
        // Force-close connections then drop the throwaway database.
        await master.ExecuteAsync(
            $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}];");
    }
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;
```

- [ ] **Step 3: Write SESSION_CONTEXT test (factory stamps tenant)**

Create `tests/Sms.Tests.Integration/Data/SessionContextTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Data;

[Collection("sql")]
public class SessionContextTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Open_stamps_tenant_id_into_session_context()
    {
        var tid = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.Set(tid, Guid.NewGuid(), isPlatform: false);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);

        await using var conn = await factory.OpenAsync();
        var read = await conn.QuerySingleAsync<Guid>(
            "SELECT CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)");
        read.Should().Be(tid);
    }
}
```

- [ ] **Step 4: Write RLS isolation test (tenant A cannot read tenant B)**

Create `tests/Sms.Tests.Integration/Data/RlsIsolationTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Data;

[Collection("sql")]
public class RlsIsolationTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Tenant_session_sees_only_its_own_users()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed as platform (bypasses RLS block predicate) so both tenants' rows exist.
        var platform = new TenantContext();
        platform.Set(null, Guid.NewGuid(), isPlatform: true);
        var platformFactory = new SqlConnectionFactory(fx.ConnectionString, platform);
        await using (var seed = await platformFactory.OpenAsync())
        {
            await seed.ExecuteAsync(
                "INSERT dbo.Tenants (Id, Name, Slug) VALUES (@a,'A',@sa),(@b,'B',@sb)",
                new { a = tenantA, b = tenantB, sa = "a-" + tenantA.ToString("N"), sb = "b-" + tenantB.ToString("N") });
            await seed.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, Email) VALUES (NEWID(),@a,'a@x.com'),(NEWID(),@b,'b@x.com')",
                new { a = tenantA, b = tenantB });
        }

        // Tenant A session: only A's user is visible.
        var aCtx = new TenantContext();
        aCtx.Set(tenantA, Guid.NewGuid(), isPlatform: false);
        var aFactory = new SqlConnectionFactory(fx.ConnectionString, aCtx);
        await using var connA = await aFactory.OpenAsync();
        var emails = await connA.QueryAsync<string>("SELECT Email FROM dbo.Users");
        emails.Should().ContainSingle().Which.Should().Be("a@x.com");
    }
}
```

- [ ] **Step 5: Run the integration tests (host SQL Server required)**

Run: `dotnet test tests/Sms.Tests.Integration`
Expected: PASS (2 tests). Requires `DESKTOP-TJL4SG6` reachable via Windows auth (the runner has rights to `CREATE DATABASE`/`DROP DATABASE`).

- [ ] **Step 6: Commit**

```bash
git add tests/Sms.Tests.Integration Sms.sln
git commit -m "test(integration): Testcontainers SQL Server fixture + SESSION_CONTEXT + RLS isolation"
```

---

## Task 13: Auth repository + token persistence (proc-backed)

**Files:**
- Create: `src/Sms.Shared.Kernel/Auth/IRefreshTokenStore.cs`, `src/Sms.Shared.Kernel/Auth/RefreshTokenStore.cs`, `src/Sms.Shared.Kernel/Auth/AuthRepository.cs`, `src/Sms.Shared.Kernel/Auth/UserRecord.cs`
- Test: `tests/Sms.Tests.Integration/Auth/AuthRepositoryTests.cs`

- [ ] **Step 1: Write the failing integration test**

Create `tests/Sms.Tests.Integration/Auth/AuthRepositoryTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class AuthRepositoryTests(SqlServerFixture fx)
{
    private SqlConnectionFactory PlatformFactory()
    {
        var ctx = new TenantContext();
        ctx.Set(null, Guid.NewGuid(), isPlatform: true);
        return new SqlConnectionFactory(fx.ConnectionString, ctx);
    }

    [Fact]
    public async Task GetByEmail_returns_seeded_user()
    {
        var factory = PlatformFactory();
        var email = $"u{Guid.NewGuid():N}@x.com";
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.Users (Id, Email, PasswordHash, IsPlatform) VALUES (NEWID(),@e,'h',1)",
                new { e = email });

        var repo = new AuthRepository(factory);
        var user = await repo.GetByEmailAsync(email);
        user!.Email.Should().Be(email);
    }

    [Fact]
    public async Task Refresh_token_insert_then_get_active_then_revoke()
    {
        var factory = PlatformFactory();
        Guid userId;
        await using (var c = await factory.OpenAsync())
            userId = await c.QuerySingleAsync<Guid>(
                "INSERT dbo.Users (Id, Email, IsPlatform) OUTPUT inserted.Id VALUES (NEWID(),@e,1)",
                new { e = $"r{Guid.NewGuid():N}@x.com" });

        var store = new RefreshTokenStore(factory);
        await store.SaveAsync(userId, "hash-1", DateTime.UtcNow.AddDays(7));
        (await store.GetActiveUserIdAsync("hash-1")).Should().Be(userId);
        await store.RevokeAsync("hash-1");
        (await store.GetActiveUserIdAsync("hash-1")).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter AuthRepositoryTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Auth/UserRecord.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public sealed record UserRecord(
    Guid Id, Guid? TenantId, string? Email, string? StudentId, string? Phone,
    string? PasswordHash, bool IsPlatform, string Status);
```

Create `src/Sms.Shared.Kernel/Auth/AuthRepository.cs`:
```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class AuthRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetByEmail", new { Email = email }, ct);
}
```

Create `src/Sms.Shared.Kernel/Auth/IRefreshTokenStore.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public interface IRefreshTokenStore
{
    Task SaveAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default);
    Task<Guid?> GetActiveUserIdAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAsync(string tokenHash, CancellationToken ct = default);
}
```

Create `src/Sms.Shared.Kernel/Auth/RefreshTokenStore.cs`:
```csharp
using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class RefreshTokenStore(IDbConnectionFactory factory) : BaseRepository(factory), IRefreshTokenStore
{
    public Task SaveAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.RefreshToken_Insert",
            new { UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt }, ct);

    public async Task<Guid?> GetActiveUserIdAsync(string tokenHash, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<Guid>("dbo.RefreshToken_GetActive", new { TokenHash = tokenHash }, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task RevokeAsync(string tokenHash, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.RefreshToken_Revoke", new { TokenHash = tokenHash }, ct);
}
```

> Note: `RefreshToken_GetActive` returns multiple columns but `GetActiveUserIdAsync` reads only `UserId`. Adjust the proc to `SELECT rt.UserId` only, or map to a record. For this plan, change the proc's `SELECT` to `SELECT rt.UserId FROM ...` so the `Guid` mapping is unambiguous. Apply that one-line edit to `RefreshToken_GetActive.sql` before running.

- [ ] **Step 4: Apply the proc tweak**

Edit `db/Sms.Migrations/procs/auth/RefreshToken_GetActive.sql` — change the `SELECT` line to:
```sql
    SELECT rt.UserId
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter AuthRepositoryTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth tests/Sms.Tests.Integration/Auth db/Sms.Migrations/procs/auth/RefreshToken_GetActive.sql
git commit -m "feat(kernel): proc-backed AuthRepository + RefreshTokenStore"
```

---

## Task 14: Tenant resolution middleware (X-Tenant-Id + JWT claim → ITenantContext)

**Files:**
- Create: `src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs`
- Test: `tests/Sms.Tests.Unit/Tenancy/TenantResolutionMiddlewareTests.cs`
- Modify: `src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj` (add `Microsoft.AspNetCore.Http.Abstractions` via framework reference)

- [ ] **Step 1: Enable ASP.NET Core types in Kernel** — add to `src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj` inside `<Project>`:

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `tests/Sms.Tests.Unit/Tenancy/TenantResolutionMiddlewareTests.cs`:
```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task Populates_context_from_jwt_tenant_and_sub_claims()
    {
        var ctx = new TenantContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("is_platform", "0"),
        }, "test"));
        http.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(http, ctx);

        ctx.TenantId.Should().Be(tenantId);
        ctx.UserId.Should().Be(userId);
        ctx.IsPlatform.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_mismatch_between_header_and_token_tenant()
    {
        var ctx = new TenantContext();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
        }, "test"));
        http.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString(); // different tenant

        var called = false;
        var mw = new TenantResolutionMiddleware(_ => { called = true; return Task.CompletedTask; });
        await mw.InvokeAsync(http, ctx);

        http.Response.StatusCode.Should().Be(403);
        called.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter TenantResolutionMiddlewareTests`
Expected: FAIL — type missing.

- [ ] **Step 4: Write implementation**

Create `src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant)
    {
        var user = http.User;
        var isPlatform = user.FindFirst("is_platform")?.Value == "1";
        Guid? userId = Guid.TryParse(user.FindFirst("sub")?.Value, out var uid) ? uid : null;
        Guid? tokenTenant = Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tt) ? tt : null;

        Guid? headerTenant = Guid.TryParse(http.Request.Headers["X-Tenant-Id"], out var ht) ? ht : null;

        // If both present and the caller is not platform, they must agree.
        if (!isPlatform && tokenTenant is { } a && headerTenant is { } b && a != b)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        tenant.Set(headerTenant ?? tokenTenant, userId, isPlatform);
        await next(http);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter TenantResolutionMiddlewareTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Tenancy/TenantResolutionMiddleware.cs src/Sms.Shared.Kernel/Sms.Shared.Kernel.csproj tests/Sms.Tests.Unit/Tenancy/TenantResolutionMiddlewareTests.cs
git commit -m "feat(kernel): tenant resolution middleware (header/token reconcile + platform)"
```

---

## Task 15: RBAC policies + RequireFeature tier-gating filter

**Files:**
- Create: `src/Sms.Shared.Kernel/Authz/Policies.cs`, `src/Sms.Shared.Kernel/Authz/RequireFeatureAttribute.cs`, `src/Sms.Shared.Kernel/Authz/ITenantFeatureSet.cs`
- Test: `tests/Sms.Tests.Unit/Authz/RequireFeatureTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Authz/RequireFeatureTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class RequireFeatureTests
{
    private sealed class FakeFeatures(params string[] on) : ITenantFeatureSet
    {
        private readonly HashSet<string> _on = new(on);
        public bool Has(string feature) => _on.Contains(feature);
    }

    [Fact]
    public void Allows_when_feature_present()
    {
        var f = new FakeFeatures("attendance.geofence");
        RequireFeature.IsAllowed(f, "attendance.geofence").Should().BeTrue();
    }

    [Fact]
    public void Denies_with_feature_locked_code_when_absent()
    {
        var f = new FakeFeatures();
        RequireFeature.IsAllowed(f, "attendance.geofence").Should().BeFalse();
        RequireFeature.LockedCode.Should().Be("feature_locked");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter RequireFeatureTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Authz/ITenantFeatureSet.cs`:
```csharp
namespace Sms.Shared.Kernel.Authz;

public interface ITenantFeatureSet { bool Has(string feature); }
```

Create `src/Sms.Shared.Kernel/Authz/RequireFeatureAttribute.cs`:
```csharp
namespace Sms.Shared.Kernel.Authz;

public static class RequireFeature
{
    public const string LockedCode = "feature_locked";
    public static bool IsAllowed(ITenantFeatureSet features, string feature) => features.Has(feature);
}

/// Endpoint filter marker — wired in Program.cs via AddEndpointFilter on grouped routes.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
```

Create `src/Sms.Shared.Kernel/Authz/Policies.cs`:
```csharp
namespace Sms.Shared.Kernel.Authz;

/// Canonical RBAC policy names mirroring the frontend permission matrices.
public static class Policies
{
    public const string PlatformOnly = "platform.only";          // Catre team
    public const string SchoolAdmin = "school.admin";
    public const string Principal = "school.principal";
    public const string Teacher = "school.teacher";
    public const string Staff = "staff";
    public const string StudentOrParent = "student.parent";

    public static readonly string[] All =
        [PlatformOnly, SchoolAdmin, Principal, Teacher, Staff, StudentOrParent];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter RequireFeatureTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Shared.Kernel/Authz tests/Sms.Tests.Unit/Authz
git commit -m "feat(kernel): RBAC policy names + RequireFeature tier-gating primitive"
```

---

## Task 16: OTP sender stub + studentId/phone proc additions

**Files:**
- Create: `src/Sms.Shared.Kernel/Auth/IOtpSender.cs`, `src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs`, `db/Sms.Migrations/procs/auth/User_GetByStudentId.sql`, `db/Sms.Migrations/procs/auth/Otp_Insert.sql`, `db/Sms.Migrations/procs/auth/Otp_GetActive.sql`
- Modify: `db/Sms.Migrations/M0003_Procs_Auth.cs` (Down drops the new procs)
- Test: `tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs`:
```csharp
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class ConsoleOtpSenderTests
{
    [Fact]
    public async Task Generates_six_digit_code_and_reports_sent()
    {
        var sender = new ConsoleOtpSender();
        var code = await sender.SendAsync("+919999999999");
        code.Should().MatchRegex("^[0-9]{6}$");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Unit --filter ConsoleOtpSenderTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write implementation**

Create `src/Sms.Shared.Kernel/Auth/IOtpSender.cs`:
```csharp
namespace Sms.Shared.Kernel.Auth;

public interface IOtpSender
{
    /// Sends an OTP to the phone and returns the plaintext code (caller hashes + stores it).
    Task<string> SendAsync(string phone, CancellationToken ct = default);
}
```

Create `src/Sms.Shared.Kernel/Auth/ConsoleOtpSender.cs`:
```csharp
using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

public sealed class ConsoleOtpSender : IOtpSender
{
    public Task<string> SendAsync(string phone, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Console.WriteLine($"[OTP] {phone} -> {code}"); // stub; replaced by real SMS provider in Phase 6
        return Task.FromResult(code);
    }
}
```

Create `db/Sms.Migrations/procs/auth/User_GetByStudentId.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.User_GetByStudentId
    @StudentId nvarchar(64),
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.StudentId = @StudentId AND u.TenantId = @TenantId;
END
```

Create `db/Sms.Migrations/procs/auth/Otp_Insert.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Otp_Insert
    @Phone nvarchar(32),
    @CodeHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.OtpCodes (Phone, CodeHash, ExpiresAt)
    VALUES (@Phone, @CodeHash, @ExpiresAt);
END
```

Create `db/Sms.Migrations/procs/auth/Otp_GetActive.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Otp_GetActive
    @Phone nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 o.Id, o.CodeHash
    FROM dbo.OtpCodes o
    WHERE o.Phone = @Phone AND o.ConsumedAt IS NULL AND o.ExpiresAt > SYSUTCDATETIME()
    ORDER BY o.CreatedAt DESC;
END
```

- [ ] **Step 4: Extend the proc-migration Down**

In `db/Sms.Migrations/M0003_Procs_Auth.cs`, add to `Down()`:
```csharp
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_GetByStudentId;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Otp_Insert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Otp_GetActive;");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Unit --filter ConsoleOtpSenderTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth db/Sms.Migrations tests/Sms.Tests.Unit/Auth/ConsoleOtpSenderTests.cs
git commit -m "feat(kernel): OTP sender stub + studentId/otp auth procs"
```

---

## Task 17: Wire Program.cs — DI, middleware order, auth endpoints, health

**Files:**
- Modify: `src/Sms.Api/Program.cs`, `src/Sms.Api/appsettings.json`, `src/Sms.Api/appsettings.Development.json`
- Create: `src/Sms.Api/Endpoints/HealthEndpoints.cs`, `src/Sms.Api/Endpoints/AuthEndpoints.cs`, `src/Sms.Api/Auth/LoginModels.cs`
- Modify: `src/Sms.Api/Sms.Api.csproj` (JWT bearer + Serilog + Swashbuckle + OpenTelemetry)
- Test: `tests/Sms.Tests.Integration/Auth/AuthFlowTests.cs`, `tests/Sms.Tests.Integration/Health/HealthEndpointTests.cs`

- [ ] **Step 1: Add API packages**

Run:
```bash
dotnet add src/Sms.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Sms.Api package Swashbuckle.AspNetCore
dotnet add src/Sms.Api package Serilog.AspNetCore
dotnet add src/Sms.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/Sms.Api package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/Sms.Api package OpenTelemetry.Exporter.Console
dotnet add tests/Sms.Tests.Integration package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: appsettings**

Replace `src/Sms.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Sql": "Server=DESKTOP-TJL4SG6;Database=Sms;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
  },
  "Jwt": {
    "Issuer": "sms",
    "Audience": "sms-apps",
    "SigningKey": "CHANGE-ME-in-user-secrets-at-least-32-bytes-long!!",
    "AccessTokenMinutes": 15
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

Create `src/Sms.Api/appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "Sql": "Server=DESKTOP-TJL4SG6;Database=Sms;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
  }
}
```

> Note: SigningKey should move to user-secrets/env (`Jwt__SigningKey`) before any shared use; the
> appsettings value is a dev placeholder only.

- [ ] **Step 3: Health endpoints**

Create `src/Sms.Api/Endpoints/HealthEndpoints.cs`:
```csharp
namespace Sms.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
    }
}
```

- [ ] **Step 4: Login models + auth endpoints**

Create `src/Sms.Api/Auth/LoginModels.cs`:
```csharp
namespace Sms.Api.Auth;

public sealed record LoginRequest(
    string? Email, string? Password, string? StudentId, string? Phone, string? Role, Guid? TenantId);

public sealed record TokenResponse(string AccessToken, string RefreshToken);
public sealed record RefreshRequest(string RefreshToken);
```

Create `src/Sms.Api/Endpoints/AuthEndpoints.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using Sms.Api.Auth;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Http;

namespace Sms.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/v1/auth");

        g.MapPost("/login", async (LoginRequest req, AuthRepository users, IPasswordHasher hasher,
            IJwtTokenService jwt, IRefreshTokenStore tokens) =>
        {
            if (req.Email is null || req.Password is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "email and password required")),
                    statusCode: 422);

            var user = await users.GetByEmailAsync(req.Email);
            if (user?.PasswordHash is null || !hasher.Verify(req.Password, user.PasswordHash))
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "bad email or password")),
                    statusCode: 401);

            var access = jwt.IssueAccess(user.Id, user.TenantId,
                roles: user.IsPlatform ? new[] { "platform.only" } : new[] { "school.admin" },
                isPlatform: user.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30));
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, refresh)));
        });

        g.MapPost("/refresh", async (RefreshRequest req, IRefreshTokenStore tokens,
            AuthRepository users, IJwtTokenService jwt) =>
        {
            var hash = Sha256(req.RefreshToken);
            var userId = await tokens.GetActiveUserIdAsync(hash);
            if (userId is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_token", "refresh token invalid")),
                    statusCode: 401);
            await tokens.RevokeAsync(hash); // rotation
            var newRefresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(userId.Value, Sha256(newRefresh), DateTime.UtcNow.AddDays(30));
            // Minimal: re-issue access with platform role flag from token store omitted for brevity.
            var access = jwt.IssueAccess(userId.Value, null, new[] { "school.admin" }, false);
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, newRefresh)));
        });

        g.MapGet("/me", (HttpContext http) =>
        {
            var sub = http.User.FindFirst("sub")?.Value;
            if (sub is null) return Results.Unauthorized();
            return Results.Ok(new DataEnvelope<object>(new
            {
                id = sub,
                tenant_id = http.User.FindFirst("tenant_id")?.Value,
                roles = http.User.FindAll("role").Select(c => c.Value).ToArray()
            }));
        }).RequireAuthorization();

        g.MapPost("/logout", async (RefreshRequest req, IRefreshTokenStore tokens) =>
        {
            await tokens.RevokeAsync(Sha256(req.RefreshToken));
            return Results.NoContent();
        });
    }

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 5: Compose Program.cs**

Replace `src/Sms.Api/Program.cs`:
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Sms.Api.Endpoints;
using Sms.Migrations;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

var conn = builder.Configuration.GetConnectionString("Sql")!;
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

DapperSnakeCaseConfig.Apply();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
    o.SerializerOptions.DictionaryKeyPolicy = new SnakeCaseNamingPolicy();
});

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IOtpSender, ConsoleOtpSender>();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IDbConnectionFactory>(sp =>
    new SqlConnectionFactory(conn, sp.GetRequiredService<ITenantContext>()));
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            RoleClaimType = "role", NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddConsoleExporter());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    MigrationRunner.Run(conn); // tables + RLS + procs on startup in dev
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // after auth: needs ClaimsPrincipal
app.UseAuthorization();

app.MapHealth();
app.MapAuth();

app.Run();

public partial class Program { }
```

- [ ] **Step 6: Write the failing integration tests**

Create `tests/Sms.Tests.Integration/Health/HealthEndpointTests.cs`:
```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sms.Tests.Integration.Health;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("environment", "Production")); // skip dev migrations
        var client = app.CreateClient();
        var res = await client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

Create `tests/Sms.Tests.Integration/Auth/AuthFlowTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.Auth;

[Collection("sql")]
public class AuthFlowTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> AppWithDb() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    [Fact]
    public async Task Login_with_seeded_user_returns_tokens_and_me_works()
    {
        // Seed a platform user with a known password.
        var hasher = new PasswordHasher();
        var ctx = new TenantContext(); ctx.Set(null, Guid.NewGuid(), true);
        var factory = new SqlConnectionFactory(fx.ConnectionString, ctx);
        var email = $"admin{Guid.NewGuid():N}@x.com";
        await using (var c = await factory.OpenAsync())
            await c.ExecuteAsync(
                "INSERT dbo.Users (Id, Email, PasswordHash, IsPlatform) VALUES (NEWID(),@e,@h,1)",
                new { e = email, h = hasher.Hash("Pass123!") });

        await using var app = AppWithDb();
        var client = app.CreateClient();

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password = "Pass123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        body!.Data.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new("Bearer", body.Data.AccessToken);
        var me = await client.GetAsync("/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record LoginEnvelope(TokenData Data);
    private sealed record TokenData(string AccessToken, string RefreshToken);
}
```

- [ ] **Step 7: Run all tests**

Run: `dotnet test`
Expected: all unit tests PASS; integration `HealthEndpointTests` + `AuthFlowTests` PASS (Docker running).

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Api tests/Sms.Tests.Integration
git commit -m "feat(api): Program composition + JWT + auth endpoints + health (login/me/refresh/logout)"
```

---

## Task 18: Module placeholders + MapGroup registration points

**Files:**
- Create: `src/Sms.Modules.Tenancy/Sms.Modules.Tenancy.csproj` + `ModuleEndpoints.cs` (one representative placeholder; repeat for the other 9 modules)
- Modify: `Sms.sln`, `src/Sms.Api/Program.cs`

- [ ] **Step 1: Create the 10 module placeholder libraries**

Run (repeat the pattern for each module name):
```bash
for m in Identity Tenancy Sis Staffing Academics Attendance Finance Transport Comms Reporting; do
  dotnet new classlib -n "Sms.Modules.$m" -o "src/Sms.Modules.$m"
  dotnet sln add "src/Sms.Modules.$m"
  dotnet add "src/Sms.Modules.$m" reference src/Sms.Shared.Kernel
  dotnet add src/Sms.Api reference "src/Sms.Modules.$m"
  rm -f "src/Sms.Modules.$m/Class1.cs"
done
```

- [ ] **Step 2: Add a representative module registration seam (Tenancy shown)**

Create `src/Sms.Modules.Tenancy/ModuleEndpoints.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Sms.Modules.Tenancy;

public static class ModuleEndpoints
{
    // Phase 1 fills this group with /v1/clients, /plans, /subscriptions, etc.
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1");
        g.MapGet("/tenancy/_ping", () => Results.Ok(new { module = "tenancy", status = "scaffold" }));
        return app;
    }
}
```

> Add the `Microsoft.AspNetCore.App` framework reference to each module csproj that maps endpoints:
> ```xml
>   <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
> ```

- [ ] **Step 3: Register the module in Program.cs**

In `src/Sms.Api/Program.cs`, after `app.MapAuth();` add:
```csharp
Sms.Modules.Tenancy.ModuleEndpoints.MapTenancyModule(app);
```

- [ ] **Step 4: Build + smoke test**

Run: `dotnet build`
Expected: `Build succeeded`. (Optional manual: `dotnet run --project src/Sms.Api` then GET `/v1/tenancy/_ping`.)

- [ ] **Step 5: Commit**

```bash
git add src tests Sms.sln
git commit -m "chore: module placeholder libraries + MapGroup registration seam"
```

---

## Task 19: Docker Compose + CI

**Files:**
- Create: `docker-compose.yml`, `.dockerignore`, `src/Sms.Api/Dockerfile`, `.github/workflows/ci.yml`

- [ ] **Step 1: Dockerfile**

Create `src/Sms.Api/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Sms.Api/Sms.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Sms.Api.dll"]
```

- [ ] **Step 2: docker-compose**

Create `docker-compose.yml`:
```yaml
services:
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Local_Dev_Pass123!"
    ports: ["1433:1433"]
  api:
    build: { context: ., dockerfile: src/Sms.Api/Dockerfile }
    depends_on: [db]
    environment:
      ConnectionStrings__Sql: "Server=db;Database=Sms;User Id=sa;Password=Local_Dev_Pass123!;TrustServerCertificate=True;Encrypt=False"
      Jwt__SigningKey: "compose-dev-signing-key-at-least-32-bytes!!"
      ASPNETCORE_ENVIRONMENT: "Development"
    ports: ["5080:8080"]
```

> Note: dev-against-`DESKTOP-TJL4SG6` uses `appsettings.Development.json` (Windows auth, host SQL Server).
> `docker compose up` uses the containerized SQL Server instead — both paths supported.

Create `.dockerignore`:
```
bin/
obj/
TestResults/
.git/
```

- [ ] **Step 3: CI**

Create `.github/workflows/ci.yml`:
```yaml
name: ci
on: [push, pull_request]
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet build --configuration Release
      - run: dotnet test tests/Sms.Tests.Unit --configuration Release
      # Integration tests use Testcontainers; the GitHub runner has Docker available.
      - run: dotnet test tests/Sms.Tests.Integration --configuration Release
```

- [ ] **Step 4: Build to verify nothing broke**

Run: `dotnet build`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml .dockerignore src/Sms.Api/Dockerfile .github
git commit -m "chore: Dockerfile + docker-compose (API + SQL Server) + CI"
```

---

## Task 20: Phase 0 full verification

- [ ] **Step 1:** `dotnet build` → `Build succeeded`, 0 warnings (warnings-as-errors on).
- [ ] **Step 2:** `dotnet test tests/Sms.Tests.Unit` → all unit tests green.
- [ ] **Step 3:** `dotnet test tests/Sms.Tests.Integration` against `DESKTOP-TJL4SG6` → all green (SESSION_CONTEXT, RLS isolation, auth repo, auth flow, health). Fixture creates/drops its own throwaway DB.
- [ ] **Step 4 (manual, host DB):** Confirm SQL Server `DESKTOP-TJL4SG6` is reachable; create the `Sms` database (`CREATE DATABASE Sms;`), then `dotnet run --project src/Sms.Api` → app starts, runs migrations, Swagger at `/swagger`, `GET /health` returns ok.
- [ ] **Step 5 (manual, compose — DEFERRED):** Docker is not installed on this machine; `docker compose up` verification is deferred until Docker is available. The `docker-compose.yml` artifact is still committed (Task 19) for later use.
- [ ] **Step 6:** `git commit --allow-empty -m "test: Phase 0 foundation verified green"`

---

## Phase 0 Self-Review

**Spec coverage (spec §1 + §3 + Phase 0 of §4):**
- SP data layer (writes/complex reads) + inline simple reads → Task 6 `BaseRepository`, Tasks 11/13/16 procs ✓
- `SESSION_CONTEXT` on connection open → Task 5, proven Task 12 ✓
- Row-Level Security policies + platform bypass → Task 10, isolation test Task 12 ✓
- FluentMigrator runs DDL + procs (CREATE OR ALTER, embedded) → Tasks 9/11/16 ✓
- JWT access + rotating refresh; email/pw + studentId/pw + phone/OTP stub → Tasks 7/8/16/17 ✓
- RBAC policy engine + tier-gating `RequireFeature` → Task 15 ✓
- Tenant resolution middleware (header/token reconcile) → Task 14 ✓
- snake_case JSON + data/error envelopes + paging → Task 3, applied Task 17 ✓
- Serilog + OpenTelemetry + health → Task 17 ✓
- Swagger → Task 17 ✓
- Docker Compose (API + SQL Server) + CI → Task 19 ✓
- Module placeholders + MapGroup seam → Task 18 ✓
- Dev target `DESKTOP-TJL4SG6` → Task 17 appsettings, Task 20 verification ✓

**Placeholder scan:** module `_ping` endpoints are intentional scaffolds (labeled), filled in later phases. The `/auth/refresh` re-issue simplification is annotated as a known minimal step to harden in Phase 1 (carry platform/tenant/roles through the token store). No "TBD/TODO" left.

**Type consistency:** `ITenantContext.Set(Guid?,Guid?,bool)` used identically in Tasks 4/5/12/14/17. `BaseRepository` helper names (`QueryProcAsync`/`QuerySingleProcAsync`/`ExecuteProcAsync`/`QueryInlineAsync`) match between Task 6 and consumers in Tasks 13/16. `TokenResponse`/`DataEnvelope<T>`/`ErrorEnvelope` consistent across Task 3 and Task 17.

**Known follow-ups into Phase 1 (not Phase 0 gaps):** refresh-token re-issue should reload the user's tenant/roles/platform flag from the store; `RequireFeature` needs an `ITenantFeatureSet` implementation backed by the tenant's active plan (Phase 1 owns plans).

---

## Phases 1–6 Roadmap (each gets its own detailed plan when reached)

Each phase below is a **future plan file** `docs/superpowers/plans/YYYY-MM-DD-backend-phaseN-<name>.md`, written from the canonical data dictionary (§3B) and route rules (§3C) of the master design doc, following the same TDD + proc-where-it-adds-value pattern proven in Phase 0. Each phase's Definition of Done: endpoints + migrations + procs + integration/contract tests green, **and the matching frontend app flipped to `DATA_SOURCE=live` and verified against its existing contract tests**.

### Phase 1 — Catre super-admin → flip `sms-catreadmin`
- **Entities/tables:** Tenants (lifecycle), Plans, Subscriptions, Invoices, BillingMandate, OnboardingItem, SupportTicket + TicketMessage, TeamMember, AuditLog (read), Reports/KPIs.
- **Procs (writes + dashboards):** `Client_Create/Update/SetStatus`, `Client_Usage`, `Invoice_MarkPaid/Refund`, `Subscription_Create`, `Onboarding_Advance`, `Ticket_AddMessage`, `Dashboard_CatreOverview` (`QueryMultiple`: MRR, signups, plan distribution), `Audit_Insert`.
- **Inline reads:** simple `/plans`, `/team` lists.
- **Endpoints:** `GET/POST/PUT/DELETE /v1/clients`, `/clients/{id}/usage`, `/clients/{id}/activity`, `/plans`, `/subscriptions`, `/invoices/{id}/mark-paid|refund`, `/onboarding`, `/tickets`, `/team`, `/reports`.
- **Cross-cutting completed:** platform-role RLS bypass + audited impersonation; `ITenantFeatureSet` backed by active plan (closes Phase 0 follow-up).
- **DoD:** `sms-catreadmin` live.

### Phase 2 — School Admin CRM → flip `sms-admin`
- **Entities:** Schools (tenant settings), Students (SIS) + enrolment, Teachers, Staff, Parents/guardians + linking, Classes/Subjects/TimetableSlot, Exam (term) + ExamPaper, Grade + report cards, AttendanceRecord (roll-call), FeeInvoice + FeePayment, HR/Payroll, ChatThread/ChatMessage, Complaint, Announcement, Approval inbox, library/transport/hostel/sports ops, Reports.
- **Procs:** all writes + report-card aggregation + fee ledger + dashboard rollups; **TVP** for bulk student import.
- **Endpoints:** per §3C — nested `/classes/{id}/students`, `/exam-papers`, `/exam-papers/{id}/grades`, `/threads`, `/approvals`, `/students` CRUD, `/fees/*`, etc.
- **DoD:** `sms-admin` live (its backend-ready plan already prepared the client).

### Phase 3 — Teacher + Principal → flip `sms-teacher-app`
- Teacher-scoped reads/writes over Phase-2 entities; **roll-call bulk-upsert proc + TVP**; marks/exam CRUD; assignments; grade upsert; **geofenced check-in** (`SESSION_CONTEXT`-verified, distance+accuracy procs); principal approvals (leave + attendance corrections); SignalR chat hub; announcements broadcast; assigned bus + live position.
- **DoD:** `sms-teacher-app` live.

### Phase 4 — Staff → flip `sms-staff`
- 6-role dashboards (polymorphic); geofenced check-in/out; **live trips** (start/end, **GPS-ping ingest via TVP** + SignalR fan-out, distance/duration summary procs); boarding roster/state; tasks; leave + balances; **phone/OTP login** (procs already exist from Phase 0).
- **DoD:** `sms-staff` live.

### Phase 5 — Student + Parent → flip `sms-student`
- Student: profile, today/schedule, subjects, homework (status/submit), grades/exams, announcements, chat. Parent: multi-child switch, child today/attendance/progress, **fees + online payment** (`IPaymentGateway` impl — provider TBD), PTM booking, transport tracking (reuse Phase-4 trips), leave for child. Student client gains refresh-token wiring.
- **DoD:** `sms-student` live.

### Phase 6 — Production hardening & scale
- Swap interfaces to managed services (Redis cache + SignalR backplane, Blob/S3, Service Bus/SQS); load test + **index & stored-procedure execution-plan tuning**; read-replica/caching for heavy reads (KPIs, GPS); stateless horizontal scale-out; DR/backup; rate-limit tuning; **RLS + RBAC penetration test**; publish OpenAPI as the frozen contract.
- **DoD:** production-ready, all 5 apps live and load-verified.

### Open confirmations to resolve before their phase
- **Phase 5:** payment provider (Razorpay-style assumed) — name it.
- **Phase 0/1 dev DB:** `DESKTOP-TJL4SG6` auth mode (Windows vs SQL login) and whether to create a dedicated `Sms` database + login. (Plan assumes Windows auth, `Database=Sms`.)
- **Auth issuer:** self-hosted JWT assumed (not external IdP).
</content>
