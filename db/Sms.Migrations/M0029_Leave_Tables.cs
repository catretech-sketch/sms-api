using FluentMigrator;

namespace Sms.Migrations;

[Migration(29, "Workflow: LeaveRequests (leave + approval inbox) with tenant RLS")]
public sealed class M0029_Leave_Tables : Migration
{
    public override void Up()
    {
        Create.Table("LeaveRequests")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RequesterId").AsGuid().Nullable()
            .WithColumn("ChildId").AsGuid().Nullable()
            .WithColumn("Type").AsString(20).NotNullable().WithDefaultValue("casual")
            .WithColumn("FromDate").AsDate().Nullable()
            .WithColumn("ToDate").AsDate().Nullable()
            .WithColumn("Reason").AsString(500).Nullable()
            .WithColumn("Substitute").AsString(120).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("pending")
            .WithColumn("AppliedOn").AsDate().Nullable()
            .WithColumn("DecidedBy").AsGuid().Nullable()
            .WithColumn("DecidedNote").AsString(500).Nullable();
        Create.Index("IX_LeaveRequests_Tenant_Status").OnTable("LeaveRequests")
            .OnColumn("TenantId").Ascending().OnColumn("Status").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.LeaveRequestsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LeaveRequests,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LeaveRequests AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.LeaveRequestsTenantPolicy;");
        Delete.Table("LeaveRequests");
    }
}
