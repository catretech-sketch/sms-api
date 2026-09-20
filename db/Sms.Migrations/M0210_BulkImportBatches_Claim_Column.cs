using FluentMigrator;

namespace Sms.Migrations;

/// Security review: the existence-check-then-insert in StudentBulkImportService was not an
/// atomic idempotency claim — two concurrent requests for the same (TenantId, ImportId,
/// BatchIndex) could both see no row, both process the whole batch (creating real students),
/// and only then race on the final INSERT; the loser's students/extras/transport rows were left
/// behind as duplicates. ResultJson becomes nullable so a batch can be claimed (a row inserted
/// with ResultJson = NULL) BEFORE processing starts — the unique index on
/// (TenantId, ImportId, BatchIndex) now guards the claim itself, not just the final write.
[Migration(210, "BulkImportBatches: ResultJson nullable, to support claim-before-process idempotency")]
public sealed class M0210_BulkImportBatches_Claim_Column : Migration
{
    public override void Up() =>
        Alter.Column("ResultJson").OnTable("BulkImportBatches").AsCustom("nvarchar(max)").Nullable();

    public override void Down() =>
        Execute.Sql(@"
            DELETE FROM dbo.BulkImportBatches WHERE ResultJson IS NULL;
            ALTER TABLE dbo.BulkImportBatches ALTER COLUMN ResultJson nvarchar(max) NOT NULL;");
}
