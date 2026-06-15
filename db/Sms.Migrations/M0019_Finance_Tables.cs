using FluentMigrator;

namespace Sms.Migrations;

[Migration(19, "Finance: FeePayments with tenant RLS")]
public sealed class M0019_Finance_Tables : Migration
{
    public override void Up()
    {
        Create.Table("FeePayments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("StudentName").AsString(200).Nullable()
            .WithColumn("ClassLabel").AsString(40).Nullable()
            .WithColumn("FeeType").AsString(20).NotNullable().WithDefaultValue("academic")
            .WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Method").AsString(40).Nullable()
            .WithColumn("Ref").AsString(80).Nullable()
            .WithColumn("Date").AsDate().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_FeePayments_Tenant_Student").OnTable("FeePayments")
            .OnColumn("TenantId").Ascending().OnColumn("StudentId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.FeePaymentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeePayments,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeePayments AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeePaymentsTenantPolicy;");
        Delete.Table("FeePayments");
    }
}
