CREATE OR ALTER PROCEDURE dbo.ExamPaper_Create
    @TenantId uniqueidentifier, @ExamId uniqueidentifier, @ClassId uniqueidentifier, @Name nvarchar(120),
    @Subject nvarchar(80), @SubjectId uniqueidentifier, @Date date, @StartTime nvarchar(10),
    @DurationMin int, @MaxMarks int, @Room nvarchar(40), @Invigilator1 nvarchar(120), @Invigilator2 nvarchar(120),
    @Topics nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.ExamPapers (Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime,
        DurationMin, MaxMarks, Room, Invigilator1, Invigilator2, Topics)
    VALUES (@Id, @TenantId, @ExamId, @ClassId, @Name, @Subject, @SubjectId, @Date, @StartTime,
        @DurationMin, ISNULL(@MaxMarks, 100), @Room, @Invigilator1, @Invigilator2, @Topics);

    SELECT Id, TenantId, ExamId, ClassId, Name, Subject, SubjectId, [Date], StartTime, DurationMin,
           MaxMarks, Room, Invigilator1, Invigilator2, Status, Topics
    FROM dbo.ExamPapers WHERE Id = @Id;
END
