# Catre Admin End-to-End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Catre platform-admin surface usable end-to-end — bootstrap the first platform admin so the surface is reachable, and replace hardcoded dashboard/revenue fields with real data.

**Architecture:** Two independently-shippable sub-projects. (1) A startup seeder creates the first `IsPlatform=1` user from config so OTP login works. (2) Dashboard/revenue repositories are rewritten to read real data; historical MRR/churn come from a forward-filling `PlatformMetricsSnapshot` table written at startup. New stored procs are embedded `.sql` resources run by FluentMigrator migrations (existing pattern). Startup routines run with a platform `ITenantContext` (RLS bypass).

**Tech Stack:** .NET 10 minimal APIs, Dapper, SQL Server, FluentMigrator, xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration tests against a real SQL Server (`SqlServerFixture`, `[Collection("sql")]`).

**Conventions verified in this codebase:**
- Procs are embedded `.sql` under `db/Sms.Migrations/procs/<folder>/`, run via `M0003_Procs_Auth.EmbeddedProcs("procs.<folder>.")`. Highest existing migration number is **34**; new ones are **35, 36, 37**.
- `BaseRepository` exposes `QueryProcAsync<T>`, `QuerySingleProcAsync<T>`, `ExecuteProcAsync`, `QueryInlineAsync<T>`.
- API responses are snake_cased automatically; records map straight from columns.
- `ITenantContext.Set(Guid? tenantId, Guid? userId, bool isPlatform)`. Platform context (`isPlatform: true`) stamps `SESSION_CONTEXT('IsPlatform')=1` which RLS policies bypass.
- Build: `dotnet build`. Tests: `dotnet test tests/Sms.Tests.Integration`. A running SQL Server matching `SqlServerFixture.ConnectionString` is required for integration tests (`docker-compose.yml`).

---

## SUB-PROJECT 1: Platform Admin Bootstrap

Independently shippable: after this, a real Catre admin can OTP-log-in and reach every `/v1` platform endpoint.

### Task 1: `dbo.PlatformAdmin_Exists` stored proc + migration

**Files:**
- Create: `db/Sms.Migrations/procs/platformadmin/PlatformAdmin_Exists.sql`
- Create: `db/Sms.Migrations/M0035_Procs_Platform_Admin.cs`

- [ ] **Step 1: Write the proc**

`db/Sms.Migrations/procs/platformadmin/PlatformAdmin_Exists.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.PlatformAdmin_Exists
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM dbo.Users WHERE IsPlatform = 1 AND Status = 'active'
    ) THEN 1 ELSE 0 END;
END
```

- [ ] **Step 2: Write the migration**

`db/Sms.Migrations/M0035_Procs_Platform_Admin.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(35, "Platform admin bootstrap proc: PlatformAdmin_Exists (embedded CREATE OR ALTER)")]
public sealed class M0035_Procs_Platform_Admin : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.platformadmin."))
            Execute.Sql(sql);
    }

    public override void Down()
        => Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlatformAdmin_Exists;");
}
```

- [ ] **Step 3: Build to verify it compiles and the resource is embedded**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded. 0 Error(s)`. (The `.sql` is auto-embedded via the existing `procs/**/*.sql` glob in the csproj.)

- [ ] **Step 4: Commit**

```bash
git add db/Sms.Migrations/procs/platformadmin/PlatformAdmin_Exists.sql db/Sms.Migrations/M0035_Procs_Platform_Admin.cs
git commit -m "feat(saas): PlatformAdmin_Exists proc + M0035 migration"
```

---

### Task 2: `PlatformAdminExistsAsync` repository method

**Files:**
- Modify: `src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs`
- Test: `tests/Sms.Tests.Integration/Saas/PlatformAdminSeedTests.cs` (created in Task 4)

- [ ] **Step 1: Add the method**

In `UserProvisioningRepository` (after `CreateUserAsync`), add:
```csharp
    /// True if at least one active platform admin exists (bootstrap idempotency guard).
    public async Task<bool> PlatformAdminExistsAsync(CancellationToken ct = default)
        => await QuerySingleProcAsync<int>("dbo.PlatformAdmin_Exists", null, ct) == 1;
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Sms.Shared.Kernel`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Sms.Shared.Kernel/Auth/UserProvisioningRepository.cs
git commit -m "feat(saas): UserProvisioningRepository.PlatformAdminExistsAsync"
```

---

### Task 3: `PlatformAdminSeeder` startup routine + wire into Program.cs

**Design note (deliberate refinement of the spec):** the spec said "fail-fast (throw) when no admin exists and config is missing." A hard throw breaks the integration-test harness (test apps boot without admin config and inject their own platform JWT). Instead: **if `Catre:AdminEmail` is configured, ensure the admin (seed if missing); if it is absent, log a loud `Warning` and skip.** This still surfaces misconfiguration in real deployments (logged at startup) without breaking tests, and the admin surface is genuinely unreachable only when the operator left config blank — which the warning names explicitly.

**Files:**
- Create: `src/Sms.Api/Auth/PlatformAdminSeeder.cs`
- Modify: `src/Sms.Api/Program.cs` (after the dev-migration block)

- [ ] **Step 1: Write the seeder**

`src/Sms.Api/Auth/PlatformAdminSeeder.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Auth;

/// Ensures exactly one Catre platform admin exists. Idempotent: runs every boot,
/// no-ops once seeded. The admin logs in via the existing email OTP flow.
public static class PlatformAdminSeeder
{
    public static async Task RunAsync(WebApplication app)
    {
        var email = app.Configuration["Catre:AdminEmail"]?.Trim();
        var phone = app.Configuration["Catre:AdminPhone"]?.Trim();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PlatformAdminSeeder");

        if (string.IsNullOrWhiteSpace(email))
        {
            log.LogWarning("No Catre:AdminEmail configured; platform admin NOT seeded. " +
                "The Catre admin surface is unreachable until a platform admin exists.");
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(null, null, isPlatform: true); // platform context => RLS bypass for the seed write
        var repo = scope.ServiceProvider.GetRequiredService<UserProvisioningRepository>();

        if (await repo.PlatformAdminExistsAsync())
        {
            log.LogInformation("Platform admin present; bootstrap skipped.");
            return;
        }

        await repo.CreateUserAsync(
            tenantId: null,
            email: email,
            phone: string.IsNullOrWhiteSpace(phone) ? null : phone,
            isPlatform: true,
            roles: ["platform.only"]);
        log.LogInformation("Seeded Catre platform admin {Email}.", email);
    }
}
```

- [ ] **Step 2: Wire into Program.cs**

In `src/Sms.Api/Program.cs`, immediately **after** the closing brace of the
`if (app.Environment.IsDevelopment()) { ... }` block (which contains
`MigrationRunner.Run(conn!)`) and **before** `app.UseSerilogRequestLogging();`, add:
```csharp
// Bootstrap the first Catre platform admin (idempotent; no-ops once one exists).
await Sms.Api.Auth.PlatformAdminSeeder.RunAsync(app);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Sms.Api`
Expected: `Build succeeded. 0 Error(s)`. (Top-level `await` is already valid in this Program.cs.)

- [ ] **Step 4: Commit**

```bash
git add src/Sms.Api/Auth/PlatformAdminSeeder.cs src/Sms.Api/Program.cs
git commit -m "feat(saas): PlatformAdminSeeder startup bootstrap wired into Program"
```

---

### Task 4: Integration test — bootstrap seeds exactly one admin, idempotently

**Files:**
- Create: `tests/Sms.Tests.Integration/Saas/PlatformAdminSeedTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/Sms.Tests.Integration/Saas/PlatformAdminSeedTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Sms.Tests.Integration.Saas;

[Collection("sql")]
public class PlatformAdminSeedTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App(string? adminEmail) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
            if (adminEmail is not null)
                b.UseSetting("Catre:AdminEmail", adminEmail);
        });

    private async Task<int> PlatformAdminCount(string email)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Users WHERE IsPlatform = 1 AND Email = @email", new { email });
    }

    [Fact]
    public async Task Boot_seeds_exactly_one_platform_admin_and_is_idempotent()
    {
        var email = $"admin-{Guid.NewGuid():N}@catre.test";

        // First boot seeds the admin (RunAsync executes during factory startup).
        await using (var app = App(email)) { _ = app.CreateClient(); }
        (await PlatformAdminCount(email)).Should().Be(1);

        // Second boot finds the admin and no-ops.
        await using (var app = App(email)) { _ = app.CreateClient(); }
        (await PlatformAdminCount(email)).Should().Be(1);
    }

    [Fact]
    public async Task Boot_without_admin_config_does_not_seed_and_does_not_throw()
    {
        // No Catre:AdminEmail -> warning + skip; app still boots and serves.
        await using var app = App(adminEmail: null);
        var client = app.CreateClient();
        var res = await client.GetAsync("/health");
        res.IsSuccessStatusCode.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails (before Tasks 1–3 are applied)**

Run: `dotnet test tests/Sms.Tests.Integration --filter PlatformAdminSeedTests`
Expected (if run before Tasks 1–3): FAIL — `PlatformAdmin_Exists` missing or no seeding. After Tasks 1–3 are committed, this is the green-confirming run.

- [ ] **Step 3: Run to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter PlatformAdminSeedTests`
Expected: PASS (2 tests).

- [ ] **Step 4: Commit**

```bash
git add tests/Sms.Tests.Integration/Saas/PlatformAdminSeedTests.cs
git commit -m "test(saas): platform admin bootstrap seeds once, idempotent, non-fatal when unconfigured"
```

---

### Task 5: Configure the real admin identity

**Files:**
- Modify: `src/Sms.Api/appsettings.json`

- [ ] **Step 1: Add the config block**

In `src/Sms.Api/appsettings.json`, add a top-level `"Catre"` section (real secrets/phone come from environment/secrets in deployment; the email is safe to commit):
```json
  "Catre": {
    "AdminEmail": "catre.tech@gmail.com",
    "AdminPhone": ""
  }
```

- [ ] **Step 2: Commit**

```bash
git add src/Sms.Api/appsettings.json
git commit -m "chore(saas): configure Catre admin identity for bootstrap"
```

**Sub-project 1 complete.** A fresh deployment now seeds `catre.tech@gmail.com` as a platform admin on boot; that admin OTP-logs-in (email) and reaches the full Catre surface.

---

## SUB-PROJECT 2: Dashboard + Revenue Real Data

Replaces eight hardcoded fields. Tier A fields read existing tables directly; Tier B (MRR-series, churn, net-growth) read a forward-filling snapshot.

### Task 6: `PlatformMetricsSnapshot` table migration

**Files:**
- Create: `db/Sms.Migrations/M0036_Platform_Metrics_Table.cs`

- [ ] **Step 1: Write the migration**

`db/Sms.Migrations/M0036_Platform_Metrics_Table.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(36, "Platform metrics snapshot: monthly MRR/active/cancelled for trend + churn")]
public sealed class M0036_Platform_Metrics_Table : Migration
{
    public override void Up()
    {
        Create.Table("PlatformMetricsSnapshot")
            .WithColumn("Month").AsDate().PrimaryKey()                 // first-of-month (UTC)
            .WithColumn("Mrr").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("ActiveClients").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CancelledClients").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down() => Delete.Table("PlatformMetricsSnapshot");
}
```

- [ ] **Step 2: Build**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add db/Sms.Migrations/M0036_Platform_Metrics_Table.cs
git commit -m "feat(saas): PlatformMetricsSnapshot table (M0036)"
```

---

### Task 7: Metrics + updated dashboard/revenue procs + migration

Defines four procs in a new `procs/platformmetrics/` folder. The updated
`Dashboard_CatreOverview` and new `Report_Revenue` live here too; because this
migration (37) runs after M0008's `Dashboard_CatreOverview` (8), `CREATE OR ALTER`
makes this version win on both fresh and existing databases.

**Files:**
- Create: `db/Sms.Migrations/procs/platformmetrics/PlatformMetrics_UpsertCurrentMonth.sql`
- Create: `db/Sms.Migrations/procs/platformmetrics/Dashboard_CatreOverview.sql`
- Create: `db/Sms.Migrations/procs/platformmetrics/Report_Revenue.sql`
- Create: `db/Sms.Migrations/M0037_Procs_Platform_Metrics.cs`

- [ ] **Step 1: Snapshot upsert proc**

`db/Sms.Migrations/procs/platformmetrics/PlatformMetrics_UpsertCurrentMonth.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.PlatformMetrics_UpsertCurrentMonth
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @month date = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
    DECLARE @mrr       decimal(18,2) = (SELECT ISNULL(SUM(CASE WHEN Status='active' THEN Mrr ELSE 0 END),0) FROM dbo.Tenants);
    DECLARE @active    int           = (SELECT COUNT(*) FROM dbo.Tenants WHERE Status='active');
    DECLARE @cancelled int           = (SELECT COUNT(*) FROM dbo.Tenants WHERE Status='cancelled');

    MERGE dbo.PlatformMetricsSnapshot AS t
    USING (SELECT @month AS Month) AS s ON t.Month = s.Month
    WHEN MATCHED THEN
        UPDATE SET Mrr = @mrr, ActiveClients = @active, CancelledClients = @cancelled
    WHEN NOT MATCHED THEN
        INSERT (Month, Mrr, ActiveClients, CancelledClients, CreatedAt)
        VALUES (@month, @mrr, @active, @cancelled, SYSUTCDATETIME());
END
```

- [ ] **Step 2: Updated dashboard overview proc (5 result sets)**

`db/Sms.Migrations/procs/platformmetrics/Dashboard_CatreOverview.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Dashboard_CatreOverview
AS
BEGIN
    SET NOCOUNT ON;

    -- Churn (month-over-month) from the two latest snapshots: newly-cancelled / prior active.
    DECLARE @currCancel int, @prevActive int, @prevCancel int;
    SELECT TOP 1 @currCancel = CancelledClients FROM dbo.PlatformMetricsSnapshot ORDER BY Month DESC;
    SELECT @prevActive = ActiveClients, @prevCancel = CancelledClients FROM (
        SELECT ActiveClients, CancelledClients, ROW_NUMBER() OVER (ORDER BY Month DESC) AS rn
        FROM dbo.PlatformMetricsSnapshot
    ) x WHERE rn = 2;
    DECLARE @newChurn int = ISNULL(@currCancel,0) - ISNULL(@prevCancel,0);
    DECLARE @churnPct decimal(9,2) =
        CASE WHEN ISNULL(@prevActive,0) > 0 THEN CAST(@newChurn AS decimal(9,2)) / @prevActive * 100 ELSE 0 END;

    -- RS1: headline counts + MRR + churn
    SELECT
        COUNT(*) AS Total,
        SUM(CASE WHEN Status = 'active'    THEN 1 ELSE 0 END) AS Active,
        SUM(CASE WHEN Status = 'trial'     THEN 1 ELSE 0 END) AS Trial,
        SUM(CASE WHEN Status = 'suspended' THEN 1 ELSE 0 END) AS Suspended,
        SUM(CASE WHEN Status = 'cancelled' THEN 1 ELSE 0 END) AS Cancelled,
        ISNULL(SUM(CASE WHEN Status = 'active' THEN Mrr ELSE 0 END), 0) AS Mrr,
        SUM(CASE WHEN Status = 'trial' THEN 1 ELSE 0 END) AS TrialsEnding,
        @churnPct AS ChurnPct
    FROM dbo.Tenants;

    -- RS2: plan mix by tier
    SELECT Tier AS Label, COUNT(*) AS Value
    FROM dbo.Tenants WHERE Tier IS NOT NULL GROUP BY Tier;

    -- RS3: recent activity (latest 20 audit entries)
    SELECT TOP 20 ActorName AS Actor, Action, Target, Kind, At
    FROM dbo.AuditLog ORDER BY At DESC;

    -- RS4: usage alerts (>= 80% of a plan limit)
    SELECT Name AS Tenant, 'students' AS Metric, StudentsCount AS Used, LimitsStudents AS [Limit],
           CAST(StudentsCount * 100 / NULLIF(LimitsStudents, 0) AS int) AS Pct
    FROM dbo.Tenants
    WHERE LimitsStudents > 0 AND StudentsCount * 100 >= LimitsStudents * 80
    UNION ALL
    SELECT Name, 'storage', CAST(StorageGb AS int), LimitsStorageGb,
           CAST(StorageGb * 100 / NULLIF(LimitsStorageGb, 0) AS int)
    FROM dbo.Tenants
    WHERE LimitsStorageGb > 0 AND StorageGb * 100 >= LimitsStorageGb * 80;

    -- RS5: last 6 months — MRR (snapshot) + signups (live from Subscriptions)
    ;WITH Months AS (
        SELECT DATEFROMPARTS(
            YEAR(DATEADD(MONTH, n, SYSUTCDATETIME())),
            MONTH(DATEADD(MONTH, n, SYSUTCDATETIME())), 1) AS M
        FROM (VALUES (-5),(-4),(-3),(-2),(-1),(0)) v(n)
    )
    SELECT
        FORMAT(m.M, 'MMM') AS Label,
        ISNULL(s.Mrr, 0) AS Mrr,
        (SELECT COUNT(*) FROM dbo.Subscriptions sub
         WHERE sub.StartedAt >= m.M AND sub.StartedAt < DATEADD(MONTH, 1, m.M)) AS Signups
    FROM Months m
    LEFT JOIN dbo.PlatformMetricsSnapshot s ON s.Month = m.M
    ORDER BY m.M;
END
```

- [ ] **Step 3: Revenue report proc (3 result sets)**

`db/Sms.Migrations/procs/platformmetrics/Report_Revenue.sql`:
```sql
CREATE OR ALTER PROCEDURE dbo.Report_Revenue
AS
BEGIN
    SET NOCOUNT ON;

    -- Net growth + churn from the two latest snapshots.
    DECLARE @currActive int, @currCancel int, @prevActive int, @prevCancel int;
    SELECT TOP 1 @currActive = ActiveClients, @currCancel = CancelledClients
    FROM dbo.PlatformMetricsSnapshot ORDER BY Month DESC;
    SELECT @prevActive = ActiveClients, @prevCancel = CancelledClients FROM (
        SELECT ActiveClients, CancelledClients, ROW_NUMBER() OVER (ORDER BY Month DESC) AS rn
        FROM dbo.PlatformMetricsSnapshot
    ) x WHERE rn = 2;
    DECLARE @netGrowth int = ISNULL(@currActive,0) - ISNULL(@prevActive,0);
    DECLARE @newChurn  int = ISNULL(@currCancel,0) - ISNULL(@prevCancel,0);
    DECLARE @churnPct decimal(9,2) =
        CASE WHEN ISNULL(@prevActive,0) > 0 THEN CAST(@newChurn AS decimal(9,2)) / @prevActive * 100 ELSE 0 END;

    -- RS1: headline (active MRR live; net growth + churn from snapshots)
    SELECT
        ISNULL(SUM(CASE WHEN Status='active' THEN Mrr ELSE 0 END),0) AS TotalMrr,
        SUM(CASE WHEN Status='active' THEN 1 ELSE 0 END) AS ActiveCount,
        @netGrowth AS NetGrowth,
        @churnPct  AS GrossChurnPct
    FROM dbo.Tenants;

    -- RS2: per-plan performance
    SELECT PlanName, COUNT(*) AS Clients, ISNULL(SUM(Mrr),0) AS Mrr
    FROM dbo.Tenants WHERE PlanName IS NOT NULL GROUP BY PlanName ORDER BY SUM(Mrr) DESC;

    -- RS3: last 6 months revenue (paid invoices by PaidOn)
    ;WITH Months AS (
        SELECT DATEFROMPARTS(
            YEAR(DATEADD(MONTH, n, SYSUTCDATETIME())),
            MONTH(DATEADD(MONTH, n, SYSUTCDATETIME())), 1) AS M
        FROM (VALUES (-5),(-4),(-3),(-2),(-1),(0)) v(n)
    )
    SELECT
        FORMAT(m.M, 'MMM') AS Label,
        (SELECT ISNULL(SUM(Amount),0) FROM dbo.Invoices inv
         WHERE inv.Status = 'paid' AND inv.PaidOn >= m.M AND inv.PaidOn < DATEADD(MONTH, 1, m.M)) AS Revenue
    FROM Months m
    ORDER BY m.M;
END
```

- [ ] **Step 4: Write the migration**

`db/Sms.Migrations/M0037_Procs_Platform_Metrics.cs`:
```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(37, "Metrics upsert + real dashboard/revenue procs (embedded CREATE OR ALTER)")]
public sealed class M0037_Procs_Platform_Metrics : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.platformmetrics."))
            Execute.Sql(sql);
    }

    public override void Down()
        => Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlatformMetrics_UpsertCurrentMonth; " +
                       "DROP PROCEDURE IF EXISTS dbo.Report_Revenue;");
    // Dashboard_CatreOverview is intentionally NOT dropped here — it predates this migration (M0008).
}
```

- [ ] **Step 5: Build**

Run: `dotnet build db/Sms.Migrations`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add db/Sms.Migrations/procs/platformmetrics db/Sms.Migrations/M0037_Procs_Platform_Metrics.cs
git commit -m "feat(saas): metrics upsert + real dashboard/revenue procs (M0037)"
```

---

### Task 8: Typed contracts for usage alerts, recent activity, monthly series

**Files:**
- Modify: `src/Sms.Modules.Tenancy/Contracts/BillingContracts.cs`

- [ ] **Step 1: Add the new records and retype `DashboardOverview`**

In `BillingContracts.cs`, replace the `DashboardOverview` record and add the new
item records. The final block reads:
```csharp
// ---- Dashboard overview ----
public sealed record DashCounts(int Total, int Active, int Trial, int Suspended, int Cancelled);
public sealed record PlanMixItem(string Label, int Value, string? Color);
public sealed record SystemHealthItem(string Name, string Status, string Latency, string Uptime);
public sealed record UsageAlertItem(string Tenant, string Metric, int Used, int Limit, int Pct);
public sealed record RecentActivityItem(string? Actor, string? Action, string? Target, string? Kind, DateTime At);

public sealed record DashboardOverview(
    DashCounts Counts, decimal Mrr, int TrialsEnding, decimal ChurnPct,
    IReadOnlyList<string> Months, IReadOnlyList<decimal> MrrSeries, IReadOnlyList<int> SignupSeries,
    IReadOnlyList<PlanMixItem> PlanMix, IReadOnlyList<UsageAlertItem> UsageAlerts,
    IReadOnlyList<SystemHealthItem> SystemHealth, IReadOnlyList<RecentActivityItem> RecentActivity);
```

- [ ] **Step 2: Build (expect the dashboard repo to now fail to compile — fixed in Task 9)**

Run: `dotnet build src/Sms.Modules.Tenancy`
Expected: FAIL — `DashboardRepository` still passes `IReadOnlyList<object>` shapes. This is expected; Task 9 fixes it. (If you prefer green-at-every-step, do Step 1 here and Task 9 Step 1 before building.)

- [ ] **Step 3: Commit (with Task 9, since they compile together)**

Defer the commit to Task 9 Step 4.

---

### Task 9: Rewrite `DashboardRepository.OverviewAsync` to map real data

**Files:**
- Modify: `src/Sms.Modules.Tenancy/Data/DashboardRepository.cs`
- Test: `tests/Sms.Tests.Integration/Catre/CatreDashboardTests.cs` (Task 11)

- [ ] **Step 1: Replace the repository body**

`src/Sms.Modules.Tenancy/Data/DashboardRepository.cs`:
```csharp
using System.Data;
using Dapper;
using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class DashboardRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record CountsRow(int Total, int Active, int Trial, int Suspended, int Cancelled,
        decimal Mrr, int TrialsEnding, decimal ChurnPct);
    private sealed record PlanMixRow(string Label, int Value);
    private sealed record MonthRow(string Label, decimal Mrr, int Signups);

    /// One round-trip: counts+churn, plan mix, recent activity, usage alerts, monthly series.
    public async Task<DashboardOverview> OverviewAsync(CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "dbo.Dashboard_CatreOverview", commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var c = await multi.ReadSingleAsync<CountsRow>();
        var mix = (await multi.ReadAsync<PlanMixRow>()).ToList();
        var activity = (await multi.ReadAsync<RecentActivityItem>()).ToList();
        var alerts = (await multi.ReadAsync<UsageAlertItem>()).ToList();
        var months = (await multi.ReadAsync<MonthRow>()).ToList();

        return new DashboardOverview(
            new DashCounts(c.Total, c.Active, c.Trial, c.Suspended, c.Cancelled),
            c.Mrr, c.TrialsEnding, c.ChurnPct,
            Months: months.Select(m => m.Label).ToList(),
            MrrSeries: months.Select(m => m.Mrr).ToList(),
            SignupSeries: months.Select(m => m.Signups).ToList(),
            PlanMix: mix.Select(m => new PlanMixItem(m.Label, m.Value, null)).ToList(),
            UsageAlerts: alerts,
            SystemHealth: [new SystemHealthItem("Database", "operational", "-", "-")],
            RecentActivity: activity);
    }
}
```

**Note on `SystemHealth`:** the live DB probe already exists at `/health/ready`.
The dashboard reports a single static "Database operational" row here (this proc
only runs when the DB is reachable, so reaching this line *is* the healthy
signal). A richer multi-component health feed is out of scope for this plan.

- [ ] **Step 2: Build**

Run: `dotnet build src/Sms.Modules.Tenancy`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit Tasks 8 + 9 together**

```bash
git add src/Sms.Modules.Tenancy/Contracts/BillingContracts.cs src/Sms.Modules.Tenancy/Data/DashboardRepository.cs
git commit -m "feat(saas): dashboard overview returns real activity/alerts/series/churn"
```

---

### Task 10: Rewrite `ReportRepository.RevenueAsync` to use `Report_Revenue`

**Files:**
- Modify: `src/Sms.Modules.Tenancy/Data/CatreOpsRepositories.cs` (the `ReportRepository` class only)

- [ ] **Step 1: Replace the `RevenueAsync` method body**

Replace the entire `ReportRepository` class with:
```csharp
public sealed class ReportRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record Headline(decimal TotalMrr, int ActiveCount, int NetGrowth, decimal GrossChurnPct);
    private sealed record PlanAgg(string PlanName, int Clients, decimal Mrr);
    private sealed record RevMonth(string Label, decimal Revenue);

    public async Task<RevenueReport> RevenueAsync(CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "dbo.Report_Revenue", commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var h = await multi.ReadSingleAsync<Headline>();
        var perPlan = (await multi.ReadAsync<PlanAgg>()).ToList();
        var series = (await multi.ReadAsync<RevMonth>()).ToList();

        var arpa = h.ActiveCount > 0 ? Math.Round(h.TotalMrr / h.ActiveCount, 2) : 0m;
        var perf = perPlan.Select(p => new PlanPerf(p.PlanName, p.Clients, p.Mrr,
            h.TotalMrr > 0 ? Math.Round(p.Mrr / h.TotalMrr * 100, 1) : 0m)).ToList();
        var byPlan = perPlan.Select(p => new PlanMixItem(p.PlanName, p.Clients, null)).ToList();

        return new RevenueReport(
            Arr: h.TotalMrr * 12,
            NetGrowth: h.NetGrowth,
            GrossChurnPct: h.GrossChurnPct,
            Arpa: arpa,
            Months: series.Select(s => s.Label).ToList(),
            RevenueSeries: series.Select(s => s.Revenue).ToList(),
            RevenueByPlan: byPlan,
            PlanPerformance: perf);
    }
}
```
Add `using System.Data;` and `using Dapper;` at the top of the file if not already present (the file uses inline queries today; `QueryMultipleAsync` needs both).

- [ ] **Step 2: Build**

Run: `dotnet build src/Sms.Modules.Tenancy`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Sms.Modules.Tenancy/Data/CatreOpsRepositories.cs
git commit -m "feat(saas): revenue report returns real series + net growth + churn"
```

---

### Task 11: `MetricsSnapshotWriter` startup routine + wire into Program.cs

**Files:**
- Create: `src/Sms.Api/Metrics/MetricsSnapshotWriter.cs`
- Modify: `src/Sms.Api/Program.cs`

- [ ] **Step 1: Write the writer**

`src/Sms.Api/Metrics/MetricsSnapshotWriter.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Metrics;

/// Upserts the current month's platform metrics snapshot at startup (idempotent).
/// Historical months accumulate boot-over-boot; the current month is always refreshed.
public static class MetricsSnapshotWriter
{
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MetricsSnapshotWriter");
        await using var scope = app.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(null, null, isPlatform: true);
        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using var conn = await factory.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(
            "dbo.PlatformMetrics_UpsertCurrentMonth",
            commandType: System.Data.CommandType.StoredProcedure));
        log.LogInformation("Platform metrics snapshot upserted for the current month.");
    }
}
```

- [ ] **Step 2: Wire into Program.cs**

In `src/Sms.Api/Program.cs`, immediately **after** the
`await Sms.Api.Auth.PlatformAdminSeeder.RunAsync(app);` line added in Task 3, add:
```csharp
// Refresh the current-month platform metrics snapshot (idempotent; feeds dashboard trend + churn).
await Sms.Api.Metrics.MetricsSnapshotWriter.RunAsync(app);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Sms.Api`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Sms.Api/Metrics/MetricsSnapshotWriter.cs src/Sms.Api/Program.cs
git commit -m "feat(saas): MetricsSnapshotWriter upserts current-month snapshot at boot"
```

---

### Task 12: Integration test — dashboard + revenue return real data

This test seeds tenants/subscriptions/invoices/audit and two snapshot rows
directly (snapshots represent prior months, which the app cannot back-date), then
asserts the dashboard and revenue endpoints reflect them.

**Files:**
- Create: `tests/Sms.Tests.Integration/Catre/CatreDashboardTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/Sms.Tests.Integration/Catre/CatreDashboardTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Tests.Integration.Catre;

[Collection("sql")]
public class CatreDashboardTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient PlatformClient(WebApplicationFactory<Program> app)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), null, ["owner"], isPlatform: true);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Dashboard_returns_real_usage_alerts_and_series()
    {
        await using var app = App();
        var client = PlatformClient(app);

        // Seed a tenant over the 80% student limit, plus a recent audit row.
        var tid = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Tenants (Id, Name, Tier, Status, Mrr, StudentsCount, LimitsStudents) " +
                "VALUES (@id, @name, 'growth', 'active', 5000, 95, 100)",
                new { id = tid, name = $"Over-Limit School {tid:N}" });
            await conn.ExecuteAsync(
                "INSERT dbo.AuditLog (Id, Action, Target, Kind, At) " +
                "VALUES (NEWID(), 'client.created', @t, 'client', SYSUTCDATETIME())",
                new { t = tid.ToString() });
        }

        var data = await Data(await client.GetAsync("/v1/dashboard/overview"), HttpStatusCode.OK);

        // Usage alert fired for the over-limit tenant.
        data.GetProperty("usage_alerts").EnumerateArray()
            .Should().Contain(a => a.GetProperty("metric").GetString() == "students"
                                && a.GetProperty("pct").GetInt32() >= 80);

        // Monthly series has 6 points (last 6 months).
        data.GetProperty("months").GetArrayLength().Should().Be(6);
        data.GetProperty("mrr_series").GetArrayLength().Should().Be(6);
        data.GetProperty("signup_series").GetArrayLength().Should().Be(6);

        // Recent activity is non-empty (the audit row we inserted).
        data.GetProperty("recent_activity").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Revenue_report_derives_net_growth_and_churn_from_snapshots()
    {
        await using var app = App();
        var client = PlatformClient(app);

        // Two prior-month snapshots: active 10 -> would compare against current month written at boot.
        // Insert an explicit previous month so the proc has a rn=2 row.
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            var lastMonth = DateTime.UtcNow.AddMonths(-1);
            var firstOfLast = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            await conn.ExecuteAsync(
                "MERGE dbo.PlatformMetricsSnapshot AS t USING (SELECT @m AS Month) s ON t.Month = s.Month " +
                "WHEN MATCHED THEN UPDATE SET Mrr=1000, ActiveClients=10, CancelledClients=1 " +
                "WHEN NOT MATCHED THEN INSERT (Month, Mrr, ActiveClients, CancelledClients) " +
                "VALUES (@m, 1000, 10, 1);",
                new { m = firstOfLast });
        }

        var data = await Data(await client.GetAsync("/v1/reports/revenue"), HttpStatusCode.OK);

        data.GetProperty("months").GetArrayLength().Should().Be(6);
        data.GetProperty("revenue_series").GetArrayLength().Should().Be(6);
        // net_growth and gross_churn_pct are present and numeric (exact value depends on current-month snapshot).
        data.TryGetProperty("net_growth", out _).Should().BeTrue();
        data.TryGetProperty("gross_churn_pct", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails before the procs/repos exist**

Run: `dotnet test tests/Sms.Tests.Integration --filter CatreDashboardTests`
Expected (before Tasks 6–11): FAIL. After Tasks 6–11 committed: this is the green run.

- [ ] **Step 3: Run to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter CatreDashboardTests`
Expected: PASS (2 tests).

- [ ] **Step 4: Commit**

```bash
git add tests/Sms.Tests.Integration/Catre/CatreDashboardTests.cs
git commit -m "test(saas): dashboard + revenue return real usage/series/churn data"
```

---

### Task 13: Full regression + final verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 2: Run the full integration suite**

Run: `dotnet test`
Expected: all tests pass — in particular existing `CatreOpsTests`, `CatreBillingTests`, `CatreClientsTests` still green (the dashboard/revenue contract shape changes are additive/typed, not removed), plus the two new test classes.

- [ ] **Step 3: Confirm no regressions in the dashboard/revenue contract consumers**

Run: `dotnet test tests/Sms.Tests.Integration --filter "Catre"`
Expected: PASS.

---

## Self-Review Notes (for the implementer)

- **Spec coverage:** Bootstrap (Sub-project 1, Tasks 1–5) ✔; RecentActivity/RevenueSeries/SignupSeries/UsageAlerts/SystemHealth (Tier A, Tasks 7–10) ✔; MrrSeries/Churn/NetGrowth via snapshot (Tier B, Tasks 6–11) ✔; deferred payment/SMS not touched ✔.
- **Known honest limitation (from spec):** MRR-series and churn show meaningful history only once ≥2 monthly snapshots exist; the current month is always live. No past data is fabricated. Tests cover the derivation by inserting explicit prior-month snapshot rows.
- **Deviation:** seeder warns-and-skips when `Catre:AdminEmail` is unset rather than throwing (keeps the test harness bootable); documented in Task 3.
- **Type consistency:** `UsageAlertItem`, `RecentActivityItem`, `MonthRow`, `Headline`, `RevMonth` column names match the proc result-set aliases exactly (`Tenant/Metric/Used/Limit/Pct`, `Actor/Action/Target/Kind/At`, `Label/Mrr/Signups`, `TotalMrr/ActiveCount/NetGrowth/GrossChurnPct`, `Label/Revenue`).
