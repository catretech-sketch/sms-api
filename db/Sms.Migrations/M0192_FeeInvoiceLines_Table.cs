using FluentMigrator;

namespace Sms.Migrations;

/// Persists the per-fee-head breakdown of a generated invoice (e.g. Tuition ₹1000 + Transport ₹500),
/// so the total can be explained after the fact and survives later Fee Head/structure edits.
/// Snapshots FeeHeadName at generation time — renaming or deleting a fee head later must not
/// change what an already-generated invoice shows.
[Migration(192, "Fees: FeeInvoiceLines table for per-fee-head invoice breakdown")]
public sealed class M0192_FeeInvoiceLines_Table : Migration
{
    public override void Up()
    {
        Create.Table("FeeInvoiceLines")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("InvoiceId").AsGuid().NotNullable()
            .WithColumn("FeeHeadId").AsGuid().Nullable()
            .WithColumn("FeeHeadName").AsString(120).NotNullable()
            .WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_FeeInvoiceLines_Tenant_Invoice").OnTable("FeeInvoiceLines")
            .OnColumn("TenantId").Ascending().OnColumn("InvoiceId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.FeeInvoiceLinesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeInvoiceLines,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeInvoiceLines AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeeInvoiceLinesTenantPolicy;");
        Delete.Table("FeeInvoiceLines");
    }
}
