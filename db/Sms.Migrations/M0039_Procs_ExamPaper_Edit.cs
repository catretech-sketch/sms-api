using FluentMigrator;

namespace Sms.Migrations;

[Migration(39, "Exam paper edit: ExamPaper_Update (partial) + ExamPaper_Delete procs")]
public sealed class M0039_Procs_ExamPaper_Edit : Migration
{
    public override void Up()
    {
        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Update
    @Id uniqueidentifier, @Name nvarchar(120) = NULL, @Subject nvarchar(80) = NULL,
    @SubjectId uniqueidentifier = NULL, @Date date = NULL, @StartTime nvarchar(10) = NULL,
    @DurationMin int = NULL, @MaxMarks int = NULL, @Room nvarchar(40) = NULL,
    @Invigilator1 nvarchar(120) = NULL, @Invigilator2 nvarchar(120) = NULL, @Status nvarchar(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ExamPapers SET
        Name = COALESCE(@Name, Name), Subject = COALESCE(@Subject, Subject),
        SubjectId = COALESCE(@SubjectId, SubjectId), [Date] = COALESCE(@Date, [Date]),
        StartTime = COALESCE(@StartTime, StartTime), DurationMin = COALESCE(@DurationMin, DurationMin),
        MaxMarks = COALESCE(@MaxMarks, MaxMarks), Room = COALESCE(@Room, Room),
        Invigilator1 = COALESCE(@Invigilator1, Invigilator1),
        Invigilator2 = COALESCE(@Invigilator2, Invigilator2), Status = COALESCE(@Status, Status)
    WHERE Id = @Id;

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status
    FROM dbo.ExamPapers WHERE Id = @Id;
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Delete @Id uniqueidentifier AS
BEGIN SET NOCOUNT ON; DELETE FROM dbo.ExamPapers WHERE Id = @Id; END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamPaper_Update;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamPaper_Delete;");
    }
}
