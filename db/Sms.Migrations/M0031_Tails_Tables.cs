using FluentMigrator;

namespace Sms.Migrations;

[Migration(31, "Tails: Complaints, Notifications, Payslips with tenant RLS")]
public sealed class M0031_Tails_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Complaints")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Subject").AsString(200).NotNullable()
            .WithColumn("From").AsString(120).Nullable()
            .WithColumn("Category").AsString(40).Nullable()
            .WithColumn("Priority").AsString(10).NotNullable().WithDefaultValue("medium")
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("open")
            .WithColumn("Age").AsString(20).Nullable()
            .WithColumn("Assignee").AsString(120).Nullable()
            .WithColumn("Body").AsString(2000).Nullable();
        Create.Index("IX_Complaints_Tenant").OnTable("Complaints").OnColumn("TenantId").Ascending();

        Create.Table("Notifications")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Icon").AsString(40).Nullable()
            .WithColumn("Tone").AsString(20).Nullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Body").AsString(1000).Nullable()
            .WithColumn("Time").AsString(40).Nullable()
            .WithColumn("Unread").AsBoolean().NotNullable().WithDefaultValue(true);
        Create.Index("IX_Notifications_Tenant").OnTable("Notifications").OnColumn("TenantId").Ascending();

        Create.Table("Payslips")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Month").AsString(20).Nullable()
            .WithColumn("Year").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Gross").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Deductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Net").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("pending");
        Create.Index("IX_Payslips_Tenant_User").OnTable("Payslips")
            .OnColumn("TenantId").Ascending().OnColumn("UserId").Ascending();

        foreach (var t in new[] { "Complaints", "Notifications", "Payslips" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "Complaints", "Notifications", "Payslips" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("Payslips");
        Delete.Table("Notifications");
        Delete.Table("Complaints");
    }
}
