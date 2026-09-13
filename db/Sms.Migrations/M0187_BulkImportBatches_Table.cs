using FluentMigrator;

namespace Sms.Migrations;

[Migration(187, "Students: BulkImportBatches table for idempotent bulk-import batch processing")]
public sealed class M0187_BulkImportBatches_Table : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF OBJECT_ID('dbo.BulkImportBatches') IS NULL
BEGIN
    CREATE TABLE dbo.BulkImportBatches (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ImportId uniqueidentifier NOT NULL,
        BatchIndex int NOT NULL,
        ResultJson nvarchar(max) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_BulkImportBatches_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_BulkImportBatches_Tenant_Import_Batch
        ON dbo.BulkImportBatches (TenantId, ImportId, BatchIndex);
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS dbo.BulkImportBatches;");
    }
}
