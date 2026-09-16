using FluentMigrator;

namespace Sms.Migrations;

[Migration(200, "Tasks: dbo.Tasks table with tenant RLS")]
public sealed class M0200_Task_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Tasks")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Detail").AsString(2000).Nullable()
            .WithColumn("Category").AsString(40).Nullable()
            .WithColumn("AssignedToUserId").AsGuid().Nullable()
            .WithColumn("AssignedToRoleKey").AsString(20).Nullable()
            .WithColumn("Priority").AsString(10).NotNullable().WithDefaultValue("normal")
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("pending")
            .WithColumn("DueDate").AsDate().Nullable()
            .WithColumn("Remarks").AsString(2000).Nullable()
            .WithColumn("PhotoUrl").AsString(int.MaxValue).Nullable()
            .WithColumn("CreatedByUserId").AsGuid().NotNullable()
            .WithColumn("CompletedByUserId").AsGuid().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("CompletedAt").AsDateTime2().Nullable();

        Create.Index("IX_Tasks_Tenant").OnTable("Tasks").OnColumn("TenantId").Ascending();
        // "My tasks" lookups: a specific-user assignment or a role broadcast, both scoped by tenant.
        Create.Index("IX_Tasks_Tenant_AssignedToUserId")
            .OnTable("Tasks").OnColumn("TenantId").Ascending().OnColumn("AssignedToUserId").Ascending();
        Create.Index("IX_Tasks_Tenant_AssignedToRoleKey")
            .OnTable("Tasks").OnColumn("TenantId").Ascending().OnColumn("AssignedToRoleKey").Ascending();

        // Exactly one of AssignedToUserId / AssignedToRoleKey must be set per row: a task is
        // either assigned to one specific person, or broadcast to everyone holding a duty role.
        // (T-SQL predicates like "IS NULL" aren't first-class boolean values, so this can't be
        // written as a direct "<>" comparison the way it could in C# — hence the OR-of-ANDs form.)
        Execute.Sql(@"
ALTER TABLE dbo.Tasks WITH CHECK ADD CONSTRAINT CK_Tasks_ExactlyOneAssignmentTarget
CHECK (
    (AssignedToUserId IS NOT NULL AND AssignedToRoleKey IS NULL)
    OR (AssignedToUserId IS NULL AND AssignedToRoleKey IS NOT NULL)
);");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.TasksTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Tasks,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Tasks AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TasksTenantPolicy;");
        Delete.Table("Tasks");
    }
}
