using FluentMigrator;

namespace Sms.Migrations;

/// dbo.FeeHead_Delete was found deployed with a stale single-parameter, IsSystem-guarded body
/// (predating tenant scoping) even though M0123 already defines the 2-parameter version in
/// source — FluentMigrator only runs each version once, so the DB never picked up the fix.
/// Re-applies M0123's intended body so it actually matches what FeeHeadRepository.DeleteAsync calls.
[Migration(186, "Finance: re-apply FeeHead_Delete(Id, TenantId) - fixes a stale pre-tenant-scoping deployment")]
public sealed class M0186_FeeHead_Delete_Fix : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Delete
    @Id uniqueidentifier,
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.FeeHeads WHERE Id = @Id AND TenantId = @TenantId;
    SELECT @@ROWCOUNT AS Deleted;
END;");
    }

    public override void Down()
    {
        // Intentionally a no-op: the pre-existing stale single-parameter body was a defect,
        // not a supported prior state, so there's nothing correct to roll back to.
    }
}
