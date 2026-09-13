using FluentMigrator;

namespace Sms.Migrations;

[Migration(180, "Traveling teachers: many-to-many teacher<->bus live-view access, distinct from the single BusAssignments duty teacher")]
public sealed class M0180_BusTravelingTeachers_Table : Migration
{
    public override void Up()
    {
        Create.Table("BusTravelingTeachers")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("TeacherUserId").AsGuid().NotNullable();
        Execute.Sql(
            "CREATE UNIQUE INDEX IX_BusTravelingTeachers_Bus_Teacher ON dbo.BusTravelingTeachers (TenantId, BusId, TeacherUserId);");
        Create.Index("IX_BusTravelingTeachers_Teacher").OnTable("BusTravelingTeachers").OnColumn("TeacherUserId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.BusTravelingTeachersTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusTravelingTeachers,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusTravelingTeachers AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.BusTravelingTeacher_Add
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @TeacherUserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM dbo.BusTravelingTeachers
        WHERE TenantId = @TenantId AND BusId = @BusId AND TeacherUserId = @TeacherUserId)
        INSERT dbo.BusTravelingTeachers (TenantId, BusId, TeacherUserId) VALUES (@TenantId, @BusId, @TeacherUserId);
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.BusTravelingTeacher_Remove
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @TeacherUserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.BusTravelingTeachers
    WHERE TenantId = @TenantId AND BusId = @BusId AND TeacherUserId = @TeacherUserId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.BusTravelingTeacher_Remove;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.BusTravelingTeacher_Add;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.BusTravelingTeachersTenantPolicy;");
        Delete.Table("BusTravelingTeachers");
    }
}
