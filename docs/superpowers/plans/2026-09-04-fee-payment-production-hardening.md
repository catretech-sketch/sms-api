# Fee Payment Production Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make offline/manual fee payment recording idempotent (no duplicate payments from double-clicks/retries) and add a minimal, reusable audit log, wired into both fee-payment write paths.

**Architecture:** A new generic `AuditLogs` table + `IAuditLogger.LogAsync(conn, tx, entry)` helper in `Sms.Shared.Kernel` that any repository can call from inside its own open transaction. Both fee-payment write paths (`FeeRepository.CreateAsync` via the `dbo.FeePayment_Create` proc, and `FeeInvoiceRepository.RecordInvoicePaymentAsync`, the raw-SQL invoice-linked path `sms-admin` actually calls) gain a client-supplied `IdempotencyKey` column with a unique index — a repeat call with the same key returns the original row instead of inserting a duplicate, and does not write a second audit row.

**Tech Stack:** .NET (C#), Dapper, FluentMigrator (raw `Execute.Sql` migrations, matching this repo's existing style), SQL Server (with row-level security), xUnit + FluentAssertions integration tests against a real SQL Server test container (`SqlServerFixture`), React/TypeScript (`sms-admin`) for the one frontend touch-point.

**Spec:** `docs/superpowers/specs/2026-09-04-fee-payment-production-hardening-design.md`

## Global Constraints

- Razorpay/online fee payment is explicitly out of scope — do not touch `feePayments.ts`'s razorpay functions, `RazorpayGateway`, or `PlanUpgradeController`.
- Refunds are out of scope — no refund endpoint exists for student fees; do not add one.
- Money columns stay `decimal(18,2)` — never introduce `float`/`double` for currency.
- All new/changed tenant-owned tables and queries must remain tenant-scoped (RLS policy + explicit `TenantId` filtering), matching the existing pattern in `M0019_Finance_Tables.cs`.
- Migrations follow this repo's established style: a `[Migration(N, "description")]` class with `Execute.Sql("""...""")` blocks using idempotent guards (`IF COL_LENGTH(...) IS NULL`, `IF NOT EXISTS (...)`), not FluentMigrator's `Create.Table`/`Alter.Table` fluent API for column adds (see `M0127_FeeInvoices_PaidAmount.cs`).
- Every repository method that writes an audit row must do so **inside the same DB transaction** as the business write it's auditing — a rollback must remove both.
- Discovered during planning (not in the original spec text, but required for the spec's intent to actually apply): the frontend's "Record Payment" button (`PaymentModal`/`WaiverModal` in `sms-admin`) calls `POST /fees/invoices/{id}/pay` → `FeeService.PayInvoiceAsync` → `FeeInvoiceRepository.RecordInvoicePaymentAsync`, **not** the standalone `POST /fees/payments` endpoint. Both paths get idempotency + audit logging in this plan so the fix actually covers the button real users click.

---

### Task 1: `AuditLogs` table migration

**Files:**
- Create: `db/Sms.Migrations/M0173_AuditLogs_Table.cs`
- Test: `tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs`

**Interfaces:**
- Produces: table `dbo.AuditLogs` with columns `Id (uniqueidentifier PK)`, `TenantId (uniqueidentifier NOT NULL)`, `ActorUserId (uniqueidentifier NULL)`, `Action (nvarchar(100) NOT NULL)`, `Module (nvarchar(50) NOT NULL)`, `EntityType (nvarchar(100) NOT NULL)`, `EntityId (nvarchar(100) NOT NULL)`, `TimestampUtc (datetime2 NOT NULL DEFAULT SYSUTCDATETIME())`, `BeforeData (nvarchar(max) NULL)`, `AfterData (nvarchar(max) NULL)`. RLS security policy `rls.AuditLogsTenantPolicy` filtering by `TenantId`, matching `rls.FeePaymentsTenantPolicy` in `M0019_Finance_Tables.cs:24-27`.

- [ ] **Step 1: Write the failing test — migration produces a queryable, tenant-filtered table**

```csharp
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class AuditLogsTests(SqlServerFixture fx)
{
    [Fact]
    public async Task AuditLogs_table_exists_with_expected_columns()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var cols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AuditLogs'")).ToList();
        cols.Should().Contain([
            "Id", "TenantId", "ActorUserId", "Action", "Module", "EntityType", "EntityId",
            "TimestampUtc", "BeforeData", "AfterData",
        ]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter AuditLogs_table_exists_with_expected_columns`
Expected: FAIL — `dbo.AuditLogs` does not exist yet (`SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS` returns zero rows, so `cols` is empty and `.Should().Contain(...)` fails).

- [ ] **Step 3: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(173, "AuditLogs: generic, reusable, insert-only audit trail with tenant RLS")]
public sealed class M0173_AuditLogs_Table : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TenantId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NULL,
        Action nvarchar(100) NOT NULL,
        Module nvarchar(50) NOT NULL,
        EntityType nvarchar(100) NOT NULL,
        EntityId nvarchar(100) NOT NULL,
        TimestampUtc datetime2 NOT NULL CONSTRAINT DF_AuditLogs_TimestampUtc DEFAULT (SYSUTCDATETIME()),
        BeforeData nvarchar(max) NULL,
        AfterData nvarchar(max) NULL
    );
    CREATE INDEX IX_AuditLogs_Tenant_Entity ON dbo.AuditLogs (TenantId, EntityType, EntityId);
END
""");

        Execute.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'AuditLogsTenantPolicy')
CREATE SECURITY POLICY rls.AuditLogsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLogs,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLogs AFTER INSERT
WITH (STATE = ON);
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AuditLogsTenantPolicy;");
        Execute.Sql("DROP TABLE IF EXISTS dbo.AuditLogs;");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

The integration test suite runs pending migrations against `SqlServerFixture`'s container automatically on startup (same as every other migration in this repo — no manual step).

Run: `dotnet test tests/Sms.Tests.Integration --filter AuditLogs_table_exists_with_expected_columns`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add db/Sms.Migrations/M0173_AuditLogs_Table.cs tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs
git commit -m "feat(audit): add AuditLogs table migration with tenant RLS"
```

---

### Task 2: `IAuditLogger` reusable helper

**Files:**
- Create: `src/Sms.Shared.Kernel/Audit/IAuditLogger.cs`
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs` (register the service near the other `Sms.Shared.Kernel` scoped registrations, e.g. after line 106's `IPaymentGateway` registration)
- Test: `tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs` (extend from Task 1)

**Interfaces:**
- Consumes: `Sms.Shared.Kernel.Data.IDbConnectionFactory` is not used directly here — this type participates in a transaction its *caller* already opened.
- Produces: `Sms.Shared.Kernel.Audit.IAuditLogger.LogAsync(DbConnection conn, DbTransaction tx, AuditEntry entry, CancellationToken ct = default)`, and `Sms.Shared.Kernel.Audit.AuditEntry(Guid TenantId, Guid? ActorUserId, string Action, string Module, string EntityType, string EntityId, object? BeforeData = null, object? AfterData = null)`. Task 3 and Task 4 call this directly with their own already-open `conn`/`tx`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs`:

```csharp
    /// <summary>Stamps SESSION_CONTEXT the same way SqlConnectionFactory does in production
    /// (see src/Sms.Shared.Kernel/Data/SqlConnectionFactory.cs:19-29), so the AuditLogs RLS
    /// filter/block predicate (rls.fn_tenant_predicate) matches this tenant.</summary>
    private static async Task StampTenantAsync(SqlConnection conn, Guid tenantId)
    {
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@v", new { v = tenantId });
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=@v", new { v = 0 });
    }

    [Fact]
    public async Task AuditLogger_writes_row_inside_caller_transaction_and_commits_with_it()
    {
        var logger = new AuditLogger();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await StampTenantAsync(conn, tenantId);
        await using var tx = await conn.BeginTransactionAsync();
        await logger.LogAsync(conn, tx, new AuditEntry(
            tenantId, actorId, "Test.Action", "Fees", "FeePayment", "entity-1",
            AfterData: new { amount = 100 }), CancellationToken.None);
        await tx.CommitAsync();

        var row = await conn.QuerySingleAsync<(Guid TenantId, Guid? ActorUserId, string Action, string Module, string EntityType, string EntityId)>(
            "SELECT TenantId, ActorUserId, Action, Module, EntityType, EntityId FROM dbo.AuditLogs WHERE TenantId = @tenantId",
            new { tenantId });
        row.ActorUserId.Should().Be(actorId);
        row.Action.Should().Be("Test.Action");
        row.Module.Should().Be("Fees");
        row.EntityType.Should().Be("FeePayment");
        row.EntityId.Should().Be("entity-1");
    }

    [Fact]
    public async Task AuditLogger_write_is_rolled_back_with_its_transaction()
    {
        var logger = new AuditLogger();
        var tenantId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await StampTenantAsync(conn, tenantId);
            await using var tx = await conn.BeginTransactionAsync();
            await logger.LogAsync(conn, tx, new AuditEntry(
                tenantId, null, "Test.RolledBack", "Fees", "FeePayment", "entity-2"), CancellationToken.None);
            await tx.RollbackAsync();
        }

        await using var verifyConn = new SqlConnection(fx.ConnectionString);
        await verifyConn.OpenAsync();
        await StampTenantAsync(verifyConn, tenantId);
        var count = await verifyConn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TenantId = @tenantId", new { tenantId });
        count.Should().Be(0);
    }
```

Add `using Microsoft.Data.SqlClient;`, `using Dapper;`, and `using Sms.Shared.Kernel.Audit;` to the top of `AuditLogsTests.cs` (the first two are very likely already present from Task 1's test in the same file — check before adding to avoid a duplicate `using`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter "AuditLogger_writes_row_inside_caller_transaction_and_commits_with_it|AuditLogger_write_is_rolled_back_with_its_transaction"`
Expected: FAIL with a compile error (`AuditLogger`/`AuditEntry`/`IAuditLogger` not found).

- [ ] **Step 3: Write the implementation**

```csharp
using System.Data.Common;
using System.Text.Json;
using Dapper;

namespace Sms.Shared.Kernel.Audit;

/// <summary>
/// A generic, insert-only audit record. No application code exposes update/delete for AuditLogs —
/// once written inside a transaction, a row is immutable and permanent.
/// </summary>
public sealed record AuditEntry(
    Guid TenantId,
    Guid? ActorUserId,
    string Action,
    string Module,
    string EntityType,
    string EntityId,
    object? BeforeData = null,
    object? AfterData = null);

/// <summary>
/// Writes an AuditLogs row using the caller's own open connection and transaction, so the audit
/// write commits or rolls back atomically with whatever business change it records. Callers are
/// expected to already be inside a transaction — this type never opens or manages one itself.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(DbConnection conn, DbTransaction tx, AuditEntry entry, CancellationToken ct = default);
}

public sealed class AuditLogger : IAuditLogger
{
    public Task LogAsync(DbConnection conn, DbTransaction tx, AuditEntry entry, CancellationToken ct = default) =>
        conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.AuditLogs (Id, TenantId, ActorUserId, Action, Module, EntityType, EntityId, BeforeData, AfterData)
            VALUES (NEWID(), @TenantId, @ActorUserId, @Action, @Module, @EntityType, @EntityId, @BeforeData, @AfterData)
            """,
            new
            {
                entry.TenantId,
                entry.ActorUserId,
                entry.Action,
                entry.Module,
                entry.EntityType,
                entry.EntityId,
                BeforeData = entry.BeforeData is null ? null : JsonSerializer.Serialize(entry.BeforeData),
                AfterData = entry.AfterData is null ? null : JsonSerializer.Serialize(entry.AfterData),
            },
            tx,
            cancellationToken: ct));
}
```

- [ ] **Step 4: Register in DI**

In `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`, add near the existing `IPaymentGateway`/`IRazorpayGateway` registrations (around line 106-109):

```csharp
        builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
```

Add `using Sms.Shared.Kernel.Audit;` to the top of the file if not already covered by an existing wildcard-style using block.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter "AuditLogger_writes_row_inside_caller_transaction_and_commits_with_it|AuditLogger_write_is_rolled_back_with_its_transaction"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Shared.Kernel/Audit/IAuditLogger.cs src/Sms.Api/Extensions/ServiceCollectionExtensions.cs tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs
git commit -m "feat(audit): add reusable IAuditLogger writing inside caller's transaction"
```

---

### Task 3: `FeePayments` schema — `CreatedAt`/`UpdatedAt`/`IdempotencyKey`

**Files:**
- Create: `db/Sms.Migrations/M0174_FeePayments_Idempotency_Columns.cs`
- Test: extend `tests/Sms.Tests.Integration/Finance/AuditLogsTests.cs` or add a focused test in a new file `tests/Sms.Tests.Integration/Finance/FeePaymentsSchemaTests.cs`

**Interfaces:**
- Produces: `dbo.FeePayments` gains `CreatedAt datetime2`, `UpdatedAt datetime2 NULL`, `IdempotencyKey uniqueidentifier NULL`, and a unique filtered index `UX_FeePayments_Tenant_IdempotencyKey` on `(TenantId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL`. Task 4 and Task 5 rely on this column and index existing.

- [ ] **Step 1: Write the failing test**

```csharp
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class FeePaymentsSchemaTests(SqlServerFixture fx)
{
    [Fact]
    public async Task FeePayments_has_idempotency_columns_and_unique_filtered_index()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var cols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FeePayments'")).ToList();
        cols.Should().Contain(["CreatedAt", "UpdatedAt", "IdempotencyKey"]);

        var indexExists = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_FeePayments_Tenant_IdempotencyKey'");
        indexExists.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_within_tenant_is_rejected_by_the_index()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var tenantId = Guid.NewGuid();
        // FeePayments has an RLS block predicate on TenantId (see M0019_Finance_Tables.cs:24-27) —
        // session context must be stamped to this tenant or the INSERT is blocked, not just the unique index.
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        var key = Guid.NewGuid();
        const string insert = """
            INSERT dbo.FeePayments (Id, TenantId, StudentId, FeeType, Amount, [Date], IdempotencyKey)
            VALUES (NEWID(), @tenantId, NEWID(), 'academic', 100, CAST(SYSUTCDATETIME() AS date), @key)
            """;
        await conn.ExecuteAsync(insert, new { tenantId, key });

        var act = () => conn.ExecuteAsync(insert, new { tenantId, key });
        await act.Should().ThrowAsync<SqlException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FeePayments_has_idempotency_columns_and_unique_filtered_index|Duplicate_idempotency_key_within_tenant_is_rejected_by_the_index"`
Expected: FAIL — `IdempotencyKey`/`CreatedAt`/`UpdatedAt` columns and the unique index don't exist yet, so the first test's `cols.Should().Contain(...)` fails and the second test's `INSERT ... IdempotencyKey` fails with "Invalid column name 'IdempotencyKey'" rather than the expected duplicate-key `SqlException`.

- [ ] **Step 3: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(174, "FeePayments: CreatedAt/UpdatedAt + IdempotencyKey with unique filtered index")]
public sealed class M0174_FeePayments_Idempotency_Columns : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'CreatedAt') IS NULL
    ALTER TABLE dbo.FeePayments ADD CreatedAt datetime2 NOT NULL
        CONSTRAINT DF_FeePayments_CreatedAt DEFAULT (SYSUTCDATETIME());
""");

        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'UpdatedAt') IS NULL
    ALTER TABLE dbo.FeePayments ADD UpdatedAt datetime2 NULL;
""");

        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'IdempotencyKey') IS NULL
    ALTER TABLE dbo.FeePayments ADD IdempotencyKey uniqueidentifier NULL;
""");

        Execute.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_FeePayments_Tenant_IdempotencyKey' AND object_id = OBJECT_ID(N'dbo.FeePayments'))
    CREATE UNIQUE INDEX UX_FeePayments_Tenant_IdempotencyKey ON dbo.FeePayments (TenantId, IdempotencyKey)
        WHERE IdempotencyKey IS NOT NULL;
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS UX_FeePayments_Tenant_IdempotencyKey ON dbo.FeePayments;");
        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'IdempotencyKey') IS NOT NULL
    ALTER TABLE dbo.FeePayments DROP COLUMN IdempotencyKey;
""");
        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'UpdatedAt') IS NOT NULL
    ALTER TABLE dbo.FeePayments DROP COLUMN UpdatedAt;
""");
        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'CreatedAt') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FeePayments DROP CONSTRAINT IF EXISTS DF_FeePayments_CreatedAt;
    ALTER TABLE dbo.FeePayments DROP COLUMN CreatedAt;
END
""");
    }
}
```

- [ ] **Step 4: Run test to verify it passes** (migration runs automatically against the test DB, same as Task 1 Step 4)

Run: same command as Step 2.
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add db/Sms.Migrations/M0174_FeePayments_Idempotency_Columns.cs tests/Sms.Tests.Integration/Finance/FeePaymentsSchemaTests.cs
git commit -m "feat(fees): add CreatedAt/UpdatedAt/IdempotencyKey to FeePayments"
```

---

### Task 4: Idempotency + audit for the invoice-linked payment path (`POST /fees/invoices/{id}/pay`)

This is the path `sms-admin`'s `PaymentModal`/`WaiverModal` actually call — the highest-value fix in this plan.

**Files:**
- Modify: `src/Sms.Modules.Finance/FinanceModule.cs`
  - `PayFeeInvoiceRequest` record (around line 60-70): add `IdempotencyKey`
  - `CreateFeePaymentRequest` record (around line 15-17): add `IdempotencyKey`
  - `FeeInvoiceRepository` constructor (line 72): add `IAuditLogger auditLogger` dependency
  - `FeeInvoiceRepository.RecordInvoicePaymentAsync` (lines 197-285): add idempotency check + audit write, add `actorUserId` parameter
- Modify: `src/Sms.Application/Services/Finance/FeeService.cs`
  - `IFeeService.PayInvoiceAsync` interface (line 20) — signature unchanged (still takes `PayFeeInvoiceRequest?`)
  - `FeeService.PayInvoiceAsync` (lines 75-132): thread `req?.IdempotencyKey` into the `CreateFeePaymentRequest` it builds (line 121-123), pass `tenant.UserId` as the new `actorUserId` argument to `RecordInvoicePaymentAsync`
- Test: `tests/Sms.Tests.Integration/Finance/FeesTests.cs`

**Interfaces:**
- Consumes: `IAuditLogger.LogAsync(DbConnection, DbTransaction, AuditEntry, CancellationToken)` from Task 2; `UX_FeePayments_Tenant_IdempotencyKey` from Task 3.
- Produces: `FeeInvoiceRepository.RecordInvoicePaymentAsync(Guid tenantId, Guid invoiceId, CreateFeePaymentRequest req, decimal amount, string method, Guid? actorUserId, CancellationToken ct = default)` — note the added `actorUserId` parameter; Task 5 does not call this method so no other call sites need updating (confirmed: `FeeService.PayInvoiceAsync` is its only caller).

- [ ] **Step 1: Write the failing test**

Add to `tests/Sms.Tests.Integration/Finance/FeesTests.cs` (reuse the `App()`/`Data()` helpers already in that file):

```csharp
    [Fact]
    public async Task Pay_invoice_with_same_idempotency_key_twice_records_payment_once()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-IDEMP-1", name = "Idem Kid", grade = "V", section = "A", roll = 9,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();

        var invoice = await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = studentId, period = "Term 1", due_date = "2026-06-01", amount = 5000,
        }), HttpStatusCode.Created);
        var invoiceId = invoice.GetProperty("id").GetGuid();

        var idempotencyKey = Guid.NewGuid();
        var body = new
        {
            amount = 2000, mode = "Cash", student_name = "Idem Kid", cls = "V-A",
            fee_type = "academic", idempotency_key = idempotencyKey,
        };

        var first = await Data(await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", body), HttpStatusCode.OK);
        var second = await Data(await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", body), HttpStatusCode.OK);

        first.GetProperty("id").GetGuid().Should().Be(second.GetProperty("id").GetGuid());

        var payments = await Data(await client.GetAsync($"/v1/fees/payments?student_id={studentId}"), HttpStatusCode.OK);
        payments.EnumerateArray().Count(p => p.GetProperty("id").GetGuid() == first.GetProperty("id").GetGuid())
            .Should().Be(1);

        var invoiceAfter = await Data(await client.GetAsync("/v1/fees/invoices"), HttpStatusCode.OK);
        invoiceAfter.EnumerateArray().First(i => i.GetProperty("id").GetGuid() == invoiceId)
            .GetProperty("paid_amount").GetDecimal().Should().Be(2000);
    }

    [Fact]
    public async Task Pay_invoice_writes_exactly_one_audit_row()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-AUDIT-1", name = "Audit Kid", grade = "V", section = "B", roll = 4,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();
        var invoice = await Data(await client.PostAsJsonAsync("/v1/fees/invoices", new
        {
            student_id = studentId, period = "Term 1", due_date = "2026-06-01", amount = 3000,
        }), HttpStatusCode.Created);
        var invoiceId = invoice.GetProperty("id").GetGuid();

        var paid = await Data(await client.PostAsJsonAsync($"/v1/fees/invoices/{invoiceId}/pay", new
        {
            amount = 3000, mode = "Cash", student_name = "Audit Kid", cls = "V-B", fee_type = "academic",
        }), HttpStatusCode.OK);
        var paymentId = paid.GetProperty("id").GetString();

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenant", new { tenant });
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.AuditLogs WHERE EntityType = 'FeePayment' AND EntityId = @paymentId",
            new { paymentId });
        count.Should().Be(1);
    }
```

`FeesTests.cs` already uses `EXEC sp_set_session_context @key=N'TenantId', @value=@tenant` before raw `SqlConnection` queries elsewhere in the file (see the existing test around line 299-304) — matching that pattern here is required, since `dbo.AuditLogs` and `dbo.FeePayments` both carry row-level-security tenant filters that block reads from a connection whose session context wasn't stamped. Add `using Dapper;` to the top of `FeesTests.cs` if not already present (check the existing `using` block — it isn't, based on the file header read earlier).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter "Pay_invoice_with_same_idempotency_key_twice_records_payment_once|Pay_invoice_writes_exactly_one_audit_row"`
Expected: FAIL — the second `/pay` call currently creates a second payment row (no idempotency), and no `AuditLogs` table is written to from this path.

- [ ] **Step 3: Add `IdempotencyKey` to the request/DTO records**

In `src/Sms.Modules.Finance/FinanceModule.cs`, modify `PayFeeInvoiceRequest` (currently lines 60-70):

```csharp
public sealed record PayFeeInvoiceRequest(
    decimal? Amount,
    string? Method,
    string? Mode,
    string? Ref,
    string? StudentName,
    string? ClassLabel,
    string? Cls,
    string? FeeType,
    string? HeadId,
    string? HeadName,
    Guid? IdempotencyKey = null);
```

Modify `CreateFeePaymentRequest` (currently lines 15-17):

```csharp
public sealed record CreateFeePaymentRequest(
    Guid StudentId, string? StudentName, string? ClassLabel, string? FeeType, decimal Amount, string? Method, string? Ref,
    Guid? InvoiceId = null, string? HeadId = null, string? HeadName = null, string? Mode = null, string? Cls = null,
    Guid? IdempotencyKey = null);
```

- [ ] **Step 4: Update `RecordInvoicePaymentAsync` for idempotency + audit**

In `src/Sms.Modules.Finance/FinanceModule.cs`, change the `FeeInvoiceRepository` class declaration (line 72) to accept the audit logger:

```csharp
public sealed class FeeInvoiceRepository(IDbConnectionFactory factory, IAuditLogger auditLogger) : BaseRepository(factory)
```

Add `using Sms.Shared.Kernel.Audit;` to the top of the file.

Replace the body of `RecordInvoicePaymentAsync` (lines 197-285) with:

```csharp
    public async Task<FeePaymentResponse?> RecordInvoicePaymentAsync(
        Guid tenantId, Guid invoiceId, CreateFeePaymentRequest req, decimal amount, string method,
        Guid? actorUserId, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        await using var conn = await Factory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (req.IdempotencyKey is { } key)
            {
                var existing = await conn.QuerySingleOrDefaultAsync<FeePaymentResponse>(new CommandDefinition(
                    """
                    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
                    FROM dbo.FeePayments WHERE TenantId = @tenantId AND IdempotencyKey = @key
                    """,
                    new { tenantId, key }, tx, cancellationToken: ct));
                if (existing is not null)
                {
                    await tx.CommitAsync(ct);
                    return existing;
                }
            }

            var inv = await conn.QuerySingleOrDefaultAsync<InvoiceLockRow>(
                new CommandDefinition(
                    """
                    SELECT Id, StudentId, Amount, Status, ISNULL(PaidAmount, 0) AS PaidAmount
                    FROM dbo.FeeInvoices WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = @invoiceId
                    """,
                    new { invoiceId }, tx, cancellationToken: ct));
            if (inv is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var remaining = Math.Max(0, inv.Amount - inv.PaidAmount);
            if (remaining <= 0 || string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var payId = Guid.NewGuid();
            var classLabel = string.IsNullOrWhiteSpace(req.ClassLabel) ? req.Cls : req.ClassLabel;
            var feeType = string.IsNullOrWhiteSpace(req.FeeType)
                ? (string.IsNullOrWhiteSpace(req.HeadName) ? "academic" : req.HeadName)
                : req.FeeType;
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId, IdempotencyKey, CreatedAt)
                VALUES (@payId, @tenantId, @StudentId, @StudentName, @classLabel, @feeType, @amount, @method, @Ref, CAST(SYSUTCDATETIME() AS date), @invoiceId, @HeadId, @IdempotencyKey, SYSUTCDATETIME())
                """,
                new
                {
                    payId,
                    tenantId,
                    inv.StudentId,
                    req.StudentName,
                    classLabel,
                    feeType,
                    amount,
                    method,
                    req.Ref,
                    invoiceId,
                    req.HeadId,
                    req.IdempotencyKey,
                }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.FeeInvoices
                SET
                    PaidAmount = ISNULL(PaidAmount, 0) + @amount,
                    Method = @method,
                    Status = CASE
                        WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN N'paid'
                        ELSE N'partial'
                    END,
                    PaidOn = CASE
                        WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN CAST(SYSUTCDATETIME() AS date)
                        ELSE PaidOn
                    END
                WHERE Id = @invoiceId
                  AND Status <> N'paid'
                """,
                new { invoiceId, amount, method }, tx, cancellationToken: ct));

            var payment = await conn.QuerySingleOrDefaultAsync<FeePaymentResponse>(new CommandDefinition(
                """
                SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
                FROM dbo.FeePayments WHERE Id = @payId
                """,
                new { payId }, tx, cancellationToken: ct));

            await auditLogger.LogAsync(conn, tx, new AuditEntry(
                tenantId, actorUserId, "FeePayment.Recorded", "Fees", "FeePayment", payId.ToString(),
                AfterData: new { Id = payId, InvoiceId = invoiceId, Amount = amount, Method = method }), ct);

            await tx.CommitAsync(ct);
            return payment;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
```

- [ ] **Step 5: Update `FeeService.PayInvoiceAsync` to thread the key and actor through**

In `src/Sms.Application/Services/Finance/FeeService.cs`, in `PayInvoiceAsync` (lines 118-126), change the `RecordInvoicePaymentAsync` call to:

```csharp
        var payment = await invoices.RecordInvoicePaymentAsync(
            tid,
            id,
            new CreateFeePaymentRequest(
                inv.StudentId, studentName, classLabel, feeType, amount, method, paymentRef,
                InvoiceId: id, HeadId: req?.HeadId, IdempotencyKey: req?.IdempotencyKey),
            amount,
            method,
            tenant.UserId,
            ct);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter "Pay_invoice_with_same_idempotency_key_twice_records_payment_once|Pay_invoice_writes_exactly_one_audit_row"`
Expected: PASS

- [ ] **Step 7: Run the full Fees integration suite to check for regressions**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~Finance"`
Expected: PASS (all existing `FeesTests`/`OwnerFeeSummaryTests` still pass — the invoice/payment shapes returned by the API are unchanged).

- [ ] **Step 8: Commit**

```bash
git add src/Sms.Modules.Finance/FinanceModule.cs src/Sms.Application/Services/Finance/FeeService.cs tests/Sms.Tests.Integration/Finance/FeesTests.cs
git commit -m "feat(fees): idempotent invoice payments with atomic audit logging"
```

---

### Task 5: Idempotency + audit for the standalone manual payment path (`POST /fees/payments`)

Not currently called by `sms-admin`, but it is a public authenticated endpoint (used by other clients, e.g. `sms-staff`) and the design spec commits to hardening it too.

**Files:**
- Modify: `db/Sms.Migrations/M0175_FeePayment_Create_Idempotent.cs` (new migration, updates the stored proc)
- Modify: `src/Sms.Modules.Finance/FinanceModule.cs`
  - `FeeRepository` constructor (line 19): add `IAuditLogger auditLogger`
  - `FeeRepository.CreateAsync` (lines 24-37): rewritten to open its own transaction, call the proc on it, conditionally audit
- Modify: `src/Sms.Application/Services/Finance/FeeService.cs`
  - `FeeService.CreatePaymentAsync` (lines 47-60): pass `tenant.UserId` as the new `actorUserId` argument
- Test: `tests/Sms.Tests.Integration/Finance/FeesTests.cs`

**Interfaces:**
- Consumes: `IAuditLogger` (Task 2), `UX_FeePayments_Tenant_IdempotencyKey` (Task 3).
- Produces: `FeeRepository.CreateAsync(Guid tenantId, CreateFeePaymentRequest r, Guid? actorUserId, CancellationToken ct = default)` — added `actorUserId` parameter; its only caller, `FeeService.CreatePaymentAsync`, is updated in this task.

- [ ] **Step 1: Write the failing test**

Add to `tests/Sms.Tests.Integration/Finance/FeesTests.cs`:

```csharp
    [Fact]
    public async Task Create_manual_payment_with_same_idempotency_key_twice_returns_same_row()
    {
        await using var app = App();
        var tenant = Guid.NewGuid();
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenant, ["school.principal"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var student = await Data(await client.PostAsJsonAsync("/v1/students", new
        {
            admission_no = "ADM-MANUAL-1", name = "Manual Kid", grade = "VI", section = "A", roll = 3,
        }), HttpStatusCode.Created);
        var studentId = student.GetProperty("id").GetGuid();

        var idempotencyKey = Guid.NewGuid();
        var body = new
        {
            student_id = studentId, student_name = "Manual Kid", class_label = "VI-A",
            fee_type = "academic", amount = 1500, method = "Cash", idempotency_key = idempotencyKey,
        };

        var first = await Data(await client.PostAsJsonAsync("/v1/fees/payments", body), HttpStatusCode.Created);
        var second = await Data(await client.PostAsJsonAsync("/v1/fees/payments", body), HttpStatusCode.Created);
        first.GetProperty("id").GetGuid().Should().Be(second.GetProperty("id").GetGuid());

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenant", new { tenant });
        var paymentCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.FeePayments WHERE Id = @id",
            new { id = first.GetProperty("id").GetGuid() });
        paymentCount.Should().Be(1);
        var auditCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.AuditLogs WHERE EntityType = 'FeePayment' AND EntityId = @id",
            new { id = first.GetProperty("id").GetString() });
        auditCount.Should().Be(1);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sms.Tests.Integration --filter Create_manual_payment_with_same_idempotency_key_twice_returns_same_row`
Expected: FAIL — currently inserts two rows.

- [ ] **Step 3: Write the migration updating `dbo.FeePayment_Create`**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(175, "FeePayment_Create: idempotent insert via IdempotencyKey + WasCreated flag")]
public sealed class M0175_FeePayment_Create_Idempotent : Migration
{
    public override void Up()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80),
    @InvoiceId uniqueidentifier = NULL, @HeadId nvarchar(64) = NULL, @IdempotencyKey uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @IdempotencyKey IS NOT NULL
    BEGIN
        DECLARE @ExistingId uniqueidentifier = (
            SELECT TOP 1 Id FROM dbo.FeePayments
            WHERE TenantId = @TenantId AND IdempotencyKey = @IdempotencyKey);
        IF @ExistingId IS NOT NULL
        BEGIN
            SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
                   CAST(0 AS bit) AS WasCreated
            FROM dbo.FeePayments WHERE Id = @ExistingId;
            RETURN;
        END
    END

    DECLARE @Id uniqueidentifier = NEWID();
    BEGIN TRY
        INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId, IdempotencyKey, CreatedAt)
        VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
            ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date), @InvoiceId, @HeadId, @IdempotencyKey, SYSUTCDATETIME());
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2601, 2627) AND @IdempotencyKey IS NOT NULL
        BEGIN
            SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
                   CAST(0 AS bit) AS WasCreated
            FROM dbo.FeePayments WHERE TenantId = @TenantId AND IdempotencyKey = @IdempotencyKey;
            RETURN;
        END
        ELSE
            THROW;
    END CATCH

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
           CAST(1 AS bit) AS WasCreated
    FROM dbo.FeePayments WHERE Id = @Id;
END
""");
    }

    public override void Down()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80),
    @InvoiceId uniqueidentifier = NULL, @HeadId nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId)
    VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
        ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date), @InvoiceId, @HeadId);

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
    FROM dbo.FeePayments WHERE Id = @Id;
END
""");
    }
}
```

- [ ] **Step 4: Rewrite `FeeRepository.CreateAsync`**

In `src/Sms.Modules.Finance/FinanceModule.cs`, change the `FeeRepository` class declaration (line 19):

```csharp
public sealed class FeeRepository(IDbConnectionFactory factory, IAuditLogger auditLogger) : BaseRepository(factory)
```

Add a private row type just above `CreateAsync` and replace `CreateAsync` (lines 24-37):

```csharp
    private sealed record FeePaymentCreateRow(
        Guid Id, Guid TenantId, Guid StudentId, string? StudentName, string? ClassLabel, string FeeType,
        decimal Amount, string? Method, string? Ref, DateTime Date, Guid? InvoiceId, string? HeadId, bool WasCreated);

    public async Task<FeePaymentResponse?> CreateAsync(
        Guid tenantId, CreateFeePaymentRequest r, Guid? actorUserId, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<FeePaymentCreateRow>(new CommandDefinition(
            "dbo.FeePayment_Create",
            new
            {
                TenantId = tenantId,
                r.StudentId,
                StudentName = r.StudentName,
                ClassLabel = string.IsNullOrWhiteSpace(r.ClassLabel) ? r.Cls : r.ClassLabel,
                FeeType = string.IsNullOrWhiteSpace(r.FeeType) ? r.HeadName : r.FeeType,
                r.Amount,
                Method = string.IsNullOrWhiteSpace(r.Method) ? r.Mode : r.Method,
                r.Ref,
                r.InvoiceId,
                r.HeadId,
                r.IdempotencyKey,
            },
            tx,
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        if (row.WasCreated)
        {
            await auditLogger.LogAsync(conn, tx, new AuditEntry(
                tenantId, actorUserId, "FeePayment.Recorded", "Fees", "FeePayment", row.Id.ToString(),
                AfterData: new { row.Id, row.Amount, row.Method, row.StudentId }), ct);
        }

        await tx.CommitAsync(ct);
        return new FeePaymentResponse(
            row.Id, row.TenantId, row.StudentId, row.StudentName, row.ClassLabel, row.FeeType,
            row.Amount, row.Method, row.Ref, row.Date, row.InvoiceId, row.HeadId);
    }
```

Add `using System.Data;` to the top of the file if not already present (needed for `CommandType.StoredProcedure`).

- [ ] **Step 5: Update `FeeService.CreatePaymentAsync`**

In `src/Sms.Application/Services/Finance/FeeService.cs`, change line 57:

```csharp
        var created = await payments.CreateAsync(tid, mapped, tenant.UserId, ct);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Sms.Tests.Integration --filter Create_manual_payment_with_same_idempotency_key_twice_returns_same_row`
Expected: PASS

- [ ] **Step 7: Run the full Fees integration suite to check for regressions**

Run: `dotnet test tests/Sms.Tests.Integration --filter "FullyQualifiedName~Finance"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add db/Sms.Migrations/M0175_FeePayment_Create_Idempotent.cs src/Sms.Modules.Finance/FinanceModule.cs src/Sms.Application/Services/Finance/FeeService.cs tests/Sms.Tests.Integration/Finance/FeesTests.cs
git commit -m "feat(fees): idempotent manual payment creation with audit logging"
```

---

### Task 6: `sms-admin` — generate and send the idempotency key

**Files:**
- Modify: `src/types/index.ts` (`FeePayment` interface, currently lines 74-92)
- Modify: `src/screens/school/finance.tsx` (`PaymentModal`, currently starting line 138; `WaiverModal`, currently starting line 317)
- Test: `src/screens/school/financeFees.test.tsx`

**Interfaces:**
- Produces: `FeePayment.idempotencyKey?: string`, sent as `idempotency_key` in the JSON body of `POST /fees/invoices/{id}/pay` (via the existing generic `camelToSnake` converter in `src/api/mapper.ts` — no mapper changes needed since it recursively converts every key).

- [ ] **Step 1: Write the failing test**

Add this test right after the existing `'records a payment with a head, mode from the full offline list, and POSTs to /fees/invoices/{id}/pay'` test (around line 215) in `src/screens/school/financeFees.test.tsx`, matching that test's exact conventions (same `renderScreen()`/`fireEvent`/`vi.mocked(fetch)` pattern):

```typescript
  it('sends a stable idempotency_key on payment submission', async () => {
    const { container } = renderScreen()
    await waitFor(() => {
      expect(within(container).getByText('Asha Verma')).toBeInTheDocument()
    })
    fireEvent.click(within(container).getAllByText('Record')[0])
    const dialog = within(container).getByRole('dialog')
    await waitFor(() => { expect(within(dialog).getByDisplayValue('Academic')).toBeInTheDocument() })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Record payment' }))

    const fetchMock = vi.mocked(fetch)
    await waitFor(() => {
      const postCall = fetchMock.mock.calls.find(([url, opts]) => opts?.method === 'POST' && String(url).includes('/fees/invoices/inv-1/pay'))
      expect(postCall).toBeDefined()
      const body = JSON.parse((postCall?.[1] as RequestInit).body as string)
      expect(typeof body.idempotency_key).toBe('string')
      expect(body.idempotency_key.length).toBeGreaterThan(0)
    })
  })

  it('approves a waiver with a stable idempotency_key', async () => {
    const { container } = renderScreen({ asRole: 'principal' })
    await waitFor(() => {
      expect(within(container).getAllByText('Waiver').length).toBeGreaterThan(0)
    })
    fireEvent.click(within(container).getAllByText('Waiver')[0])
    const dialog = within(container).getByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve waiver' }))

    const fetchMock = vi.mocked(fetch)
    await waitFor(() => {
      const postCall = fetchMock.mock.calls.find(([url, opts]) => opts?.method === 'POST' && String(url).includes('/fees/invoices/inv-1/pay'))
      expect(postCall).toBeDefined()
      const body = JSON.parse((postCall?.[1] as RequestInit).body as string)
      expect(typeof body.idempotency_key).toBe('string')
      expect(body.idempotency_key.length).toBeGreaterThan(0)
    })
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- financeFees.test.tsx`
Expected: FAIL — `body.idempotency_key` is `undefined` today, so `typeof body.idempotency_key` is `'undefined'`, not `'string'`.

- [ ] **Step 3: Add the field to the type**

In `src/types/index.ts`, add to `FeePayment` (currently lines 74-92):

```typescript
export interface FeePayment {
  id: number
  invoiceId?: string
  studentId: string
  studentName: string
  cls: string
  /** @deprecated prefer headId */
  feeType?: FeeType | string
  headId?: string
  headName?: string
  amount: number
  mode: string
  ref: string
  date: string
  note?: string
  collectedBy?: string
  cheque?: FeeCheque
  gateway?: FeeGateway
  idempotencyKey?: string
}
```

- [ ] **Step 4: Generate a stable key per modal session in `PaymentModal`**

In `src/screens/school/finance.tsx`, inside `PaymentModal` (the component starting around line 138), add near the other `useState` declarations (around line 154-161):

```typescript
  const idempotencyKeyRef = useRef(crypto.randomUUID())
```

`useRef` is already imported on line 7 (`import { useEffect, useMemo, useRef, useState, type ComponentType } from 'react'`) — no import change needed.

In the `payment` object built inside `submit` (currently lines 198-207), add the field:

```typescript
    const payment: FeePayment = {
      id: Date.now(), invoiceId: invoice.id, studentId: invoice.studentId, studentName: invoice.studentName, cls: invoice.cls,
      headId: headId === FEE_TYPE_ALL ? undefined : (headId || undefined),
      headName,
      feeType: headName,
      amount: n, mode,
      ref: paymentRef,
      date: localDateIso(),
      idempotencyKey: idempotencyKeyRef.current,
      ...(mode === 'Cheque' ? { cheque: { number: chequeNumber.trim(), bank: chequeBank.trim() || undefined, date: chequeDate || undefined } } : {}),
    }
```

- [ ] **Step 5: Same change in `WaiverModal`**

In `WaiverModal` (starting around line 317), add the same `useRef` declaration near its other `useState` calls (around line 320-321), and add `idempotencyKey: idempotencyKeyRef.current` to the `payment` object built in its `submit` (currently around lines 327-331).

- [ ] **Step 6: Wire the field through `payInvoice`**

Check `src/api/feePayments.ts`'s `payInvoice` function (lines 27-38) — it already spreads `...payment` through `camelToSnake(...)` before sending the body, so `idempotencyKey` on the `FeePayment` object will automatically become `idempotency_key` in the request with no changes needed to that file. Confirm this by reading the file once more after Step 4/5's edits are in place; if the spread is narrower than `...payment` (i.e., it explicitly lists fields), add `idempotencyKey: payment.idempotencyKey` to that explicit list instead.

- [ ] **Step 7: Run test to verify it passes**

Run: `npm test -- financeFees.test.tsx`
Expected: PASS

- [ ] **Step 8: Run the full frontend test suite to check for regressions**

Run: `npm test`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/types/index.ts src/screens/school/finance.tsx src/screens/school/financeFees.test.tsx
git commit -m "feat(fees): send a stable idempotency key with payment/waiver submissions"
```

---

### Task 7: Full verification pass

**Files:** none (verification only)

- [ ] **Step 1: Backend build**

Run (from `sms-backend`): `dotnet build`
Expected: 0 errors.

- [ ] **Step 2: Backend full test suite**

Run (from `sms-backend`): `dotnet test`
Expected: all tests pass, including every test added in Tasks 1-5 and the pre-existing `FeesTests`/`OwnerFeeSummaryTests`.

- [ ] **Step 3: Frontend typecheck and build**

Run (from `sms-admin`): `npm run typecheck` then `npm run build`
Expected: 0 errors.

- [ ] **Step 4: Frontend full test suite**

Run (from `sms-admin`): `npm test`
Expected: all tests pass, including `financeFees.test.tsx`.

- [ ] **Step 5: Report results**

Summarize actual pass/fail counts for each of the four commands above — do not report success unless each command's real output was observed.
