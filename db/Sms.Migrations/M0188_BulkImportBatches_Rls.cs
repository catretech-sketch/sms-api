using FluentMigrator;

namespace Sms.Migrations;

/// M0187 created dbo.BulkImportBatches with a TenantId column but — unlike every other
/// tenant-scoped table in this database — never attached the standard RLS security policy, so
/// BulkImportRepository's own `WHERE TenantId = @tenantId` was the only thing keeping one
/// tenant's recorded batch results out of another's. This restores the codebase-wide
/// defence-in-depth pattern (same rls.fn_tenant_predicate, same policy shape as
/// M0164_LeaveEntitlements_Table / M0167_StaffDocuments_Table) for that table.
[Migration(188, "Students: attach tenant RLS security policy to BulkImportBatches")]
public sealed class M0188_BulkImportBatches_Rls : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'BulkImportBatchesTenantPolicy')
EXEC('
CREATE SECURITY POLICY rls.BulkImportBatchesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BulkImportBatches,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BulkImportBatches AFTER INSERT
WITH (STATE = ON);');");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.BulkImportBatchesTenantPolicy;");
    }
}
