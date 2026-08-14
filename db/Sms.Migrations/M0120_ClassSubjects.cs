using FluentMigrator;

namespace Sms.Migrations;

[Migration(120, "ClassSubjects: per-class subject lists from school admin (GET/PUT /classes/{id}/subjects)")]
public sealed class M0120_ClassSubjects : Migration
{
    public override void Up()
    {
        Create.Table("ClassSubjects")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ClassId").AsGuid().NotNullable()
            .WithColumn("SubjectId").AsGuid().Nullable()
            .WithColumn("Name").AsString(80).NotNullable();
        Create.Index("IX_ClassSubjects_Class").OnTable("ClassSubjects").OnColumn("ClassId").Ascending();
        Create.UniqueConstraint("UQ_ClassSubjects_Class_Name")
            .OnTable("ClassSubjects").Columns("TenantId", "ClassId", "Name");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.ClassSubjectsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ClassSubjects,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ClassSubjects AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ClassSubject_List
    @ClassId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Name FROM dbo.ClassSubjects WHERE ClassId = @ClassId ORDER BY Name;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ClassSubject_Replace
    @TenantId uniqueidentifier,
    @ClassId uniqueidentifier,
    @NamesJson nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.ClassSubjects WHERE ClassId = @ClassId AND TenantId = @TenantId;

    IF @NamesJson IS NOT NULL AND LTRIM(RTRIM(@NamesJson)) NOT IN (N'', N'[]', N'null')
    BEGIN
        INSERT dbo.ClassSubjects (Id, TenantId, ClassId, SubjectId, Name)
        SELECT NEWID(), @TenantId, @ClassId, sub.Id, LTRIM(RTRIM(j.[value]))
        FROM OPENJSON(@NamesJson) j
        LEFT JOIN dbo.Subjects sub
            ON sub.TenantId = @TenantId AND LOWER(LTRIM(RTRIM(sub.Name))) = LOWER(LTRIM(RTRIM(j.[value])))
        WHERE LTRIM(RTRIM(ISNULL(j.[value], N''))) <> N'';
    END

    SELECT Name FROM dbo.ClassSubjects WHERE ClassId = @ClassId ORDER BY Name;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ClassSubject_Replace;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ClassSubject_List;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ClassSubjectsTenantPolicy;");
        Delete.Table("ClassSubjects");
    }
}
