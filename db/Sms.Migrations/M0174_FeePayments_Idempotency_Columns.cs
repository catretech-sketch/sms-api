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
