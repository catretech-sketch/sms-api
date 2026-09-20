using FluentMigrator;

namespace Sms.Migrations;

[Migration(43, "Academics: Assignments table + tenant RLS + insert proc")]
public sealed class M0043_Assignments_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Assignments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("ClassId").AsGuid().Nullable()
            .WithColumn("ClassName").AsString(80).Nullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("DueDate").AsDate().Nullable()
            .WithColumn("Description").AsString(int.MaxValue).Nullable()
            .WithColumn("ImageUri").AsString(400).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active");

        Create.Index("IX_Assignments_Tenant").OnTable("Assignments").OnColumn("TenantId").Ascending();

        Execute.Sql(@"CREATE SECURITY POLICY rls.AssignmentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Assignments,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Assignments AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.Assignment_Create
    @TenantId uniqueidentifier, @Title nvarchar(200),
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL,
    @Subject nvarchar(80) = NULL, @DueDate date = NULL,
    @Description nvarchar(max) = NULL, @ImageUri nvarchar(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.Assignments (TenantId, Title, ClassId, ClassName, Subject, DueDate, Description, ImageUri)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Title, @ClassId, @ClassName, @Subject, @DueDate, @Description, @ImageUri);
    SELECT Id, TenantId, Title, ClassId, ClassName, Subject, DueDate,
           0 AS SubmissionsCount, 0 AS TotalStudents, Status, Description, ImageUri
    FROM dbo.Assignments WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Assignment_Create;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AssignmentsTenantPolicy;");
        Delete.Table("Assignments");
    }
}
