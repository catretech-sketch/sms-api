using FluentMigrator;

namespace Sms.Migrations;

[Migration(25, "Phase 5: Homework + FeeInvoices (student/parent) with tenant RLS")]
public sealed class M0025_Phase5_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Homework")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("AssignmentId").AsGuid().Nullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("SubjectId").AsGuid().Nullable()
            .WithColumn("DueDate").AsDate().Nullable()
            .WithColumn("DueTime").AsString(10).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("todo")
            .WithColumn("Priority").AsString(10).NotNullable().WithDefaultValue("med")
            .WithColumn("Grade").AsString(4).Nullable();
        Create.Index("IX_Homework_Tenant_Student").OnTable("Homework")
            .OnColumn("TenantId").Ascending().OnColumn("StudentId").Ascending();

        Create.Table("FeeInvoices")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Period").AsString(60).Nullable()
            .WithColumn("DueDate").AsDate().Nullable()
            .WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(10).NotNullable().WithDefaultValue("due")
            .WithColumn("PaidOn").AsDate().Nullable()
            .WithColumn("Method").AsString(40).Nullable();
        Create.Index("IX_FeeInvoices_Tenant_Student").OnTable("FeeInvoices")
            .OnColumn("TenantId").Ascending().OnColumn("StudentId").Ascending();

        foreach (var t in new[] { "Homework", "FeeInvoices" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeeInvoicesTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.HomeworkTenantPolicy;");
        Delete.Table("FeeInvoices");
        Delete.Table("Homework");
    }
}
