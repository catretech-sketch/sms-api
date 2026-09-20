using FluentMigrator;

namespace Sms.Migrations;

[Migration(124, "ExamClasses junction for exam class_ids scope")]
public sealed class M0124_ExamClassIds : Migration
{
    public override void Up()
    {
        Create.Table("ExamClasses")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ExamId").AsGuid().NotNullable()
            .WithColumn("ClassId").AsGuid().NotNullable();
        Create.Index("IX_ExamClasses_Exam").OnTable("ExamClasses").OnColumn("ExamId").Ascending();
        Create.UniqueConstraint("UQ_ExamClasses_Exam_Class")
            .OnTable("ExamClasses").Columns("TenantId", "ExamId", "ClassId");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.ExamClassesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ExamClasses,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ExamClasses AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ExamClass_List
  @ExamId uniqueidentifier
AS
BEGIN
  SET NOCOUNT ON;
  SELECT ClassId FROM dbo.ExamClasses WHERE ExamId = @ExamId;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ExamClass_Replace
  @TenantId uniqueidentifier,
  @ExamId uniqueidentifier,
  @ClassIdsJson nvarchar(max)
AS
BEGIN
  SET NOCOUNT ON;
  DELETE FROM dbo.ExamClasses WHERE TenantId = @TenantId AND ExamId = @ExamId;
  IF @ClassIdsJson IS NOT NULL AND LTRIM(RTRIM(@ClassIdsJson)) NOT IN (N'', N'[]', N'null')
  BEGIN
    INSERT dbo.ExamClasses (Id, TenantId, ExamId, ClassId)
    SELECT NEWID(), @TenantId, @ExamId, TRY_CONVERT(uniqueidentifier, LTRIM(RTRIM(j.[value])))
    FROM OPENJSON(@ClassIdsJson) j
    WHERE TRY_CONVERT(uniqueidentifier, LTRIM(RTRIM(j.[value]))) IS NOT NULL;
  END
  SELECT ClassId FROM dbo.ExamClasses WHERE ExamId = @ExamId;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamClass_Replace;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamClass_List;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ExamClassesTenantPolicy;");
        Delete.Table("ExamClasses");
    }
}
