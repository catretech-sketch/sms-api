using FluentMigrator;

namespace Sms.Migrations;

[Migration(145, "Assignments: nvarchar(max) ImageUri, Period, Assignment_Update")]
public sealed class M0145_Assignment_Image_Period_Update : Migration
{
    private const string AssignmentCreate = """
        CREATE OR ALTER PROCEDURE dbo.Assignment_Create
            @TenantId uniqueidentifier, @Title nvarchar(200),
            @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL,
            @Subject nvarchar(80) = NULL, @DueDate date = NULL,
            @Description nvarchar(max) = NULL, @ImageUri nvarchar(max) = NULL,
            @Period int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @ins TABLE (Id uniqueidentifier);
            INSERT dbo.Assignments (TenantId, Title, ClassId, ClassName, Subject, DueDate, Description, ImageUri, Period)
            OUTPUT inserted.Id INTO @ins
            VALUES (@TenantId, @Title, @ClassId, @ClassName, @Subject, @DueDate, @Description, @ImageUri, @Period);
            SELECT Id, TenantId, Title, ClassId, ClassName, Subject, DueDate,
                   0 AS SubmissionsCount, 0 AS TotalStudents, Status, Description, ImageUri, Period
            FROM dbo.Assignments WHERE Id = (SELECT Id FROM @ins);
        END
        """;

    private const string AssignmentUpdate = """
        CREATE OR ALTER PROCEDURE dbo.Assignment_Update
            @Id uniqueidentifier,
            @Title nvarchar(200),
            @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL,
            @Subject nvarchar(80) = NULL, @DueDate date = NULL,
            @Description nvarchar(max) = NULL, @ImageUri nvarchar(max) = NULL,
            @Period int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE dbo.Assignments SET
                Title = @Title,
                ClassId = @ClassId,
                ClassName = @ClassName,
                Subject = @Subject,
                DueDate = @DueDate,
                Description = @Description,
                ImageUri = @ImageUri,
                Period = @Period
            WHERE Id = @Id;

            SELECT Id, TenantId, Title, ClassId, ClassName, Subject, DueDate,
                   0 AS SubmissionsCount, 0 AS TotalStudents, Status, Description, ImageUri, Period
            FROM dbo.Assignments WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        Alter.Column("ImageUri").OnTable("Assignments").AsString(int.MaxValue).Nullable();

        if (!Schema.Table("Assignments").Column("Period").Exists())
            Alter.Table("Assignments").AddColumn("Period").AsInt32().Nullable();

        Execute.Sql(AssignmentCreate);
        Execute.Sql(AssignmentUpdate);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Assignment_Update;");
        if (Schema.Table("Assignments").Column("Period").Exists())
            Delete.Column("Period").FromTable("Assignments");
        Alter.Column("ImageUri").OnTable("Assignments").AsString(400).Nullable();
    }
}
