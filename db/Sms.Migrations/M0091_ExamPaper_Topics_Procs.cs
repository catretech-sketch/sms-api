using FluentMigrator;

namespace Sms.Migrations;

[Migration(91, "ExamPaper_Create/Update: add Topics parameter (embedded/inline CREATE OR ALTER)")]
public sealed class M0091_ExamPaper_Topics_Procs : Migration
{
    public override void Up()
    {
        // Kept under procs/examsidentity (not procs/exams) so M0018's broad "procs.exams."
        // EmbeddedProcs fragment doesn't pick this body up and re-create it referencing
        // Topics ~70 migrations before M0090 adds the column - the same ordering pitfall
        // fixed for M0086/M0087/M0089.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.examsidentity.ExamPaper_Create"))
            Execute.Sql(sql);

        // ExamPaper_Update is inline SQL (not an embedded resource), so no broad-prefix
        // collision risk here - safe to just re-run it with the new parameter added.
        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamPaper_Update
    @Id uniqueidentifier, @Name nvarchar(120) = NULL, @Subject nvarchar(80) = NULL,
    @SubjectId uniqueidentifier = NULL, @Date date = NULL, @StartTime nvarchar(10) = NULL,
    @DurationMin int = NULL, @MaxMarks int = NULL, @Room nvarchar(40) = NULL,
    @Invigilator1 nvarchar(120) = NULL, @Invigilator2 nvarchar(120) = NULL, @Status nvarchar(20) = NULL,
    @Topics nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ExamPapers SET
        Name = COALESCE(@Name, Name), Subject = COALESCE(@Subject, Subject),
        SubjectId = COALESCE(@SubjectId, SubjectId), [Date] = COALESCE(@Date, [Date]),
        StartTime = COALESCE(@StartTime, StartTime), DurationMin = COALESCE(@DurationMin, DurationMin),
        MaxMarks = COALESCE(@MaxMarks, MaxMarks), Room = COALESCE(@Room, Room),
        Invigilator1 = COALESCE(@Invigilator1, Invigilator1),
        Invigilator2 = COALESCE(@Invigilator2, Invigilator2), Status = COALESCE(@Status, Status),
        Topics = COALESCE(@Topics, Topics)
    WHERE Id = @Id;

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status, Topics
    FROM dbo.ExamPapers WHERE Id = @Id;
END;");
    }

    public override void Down()
    {
        // No-op: previous proc bodies are superseded, not restored.
    }
}
