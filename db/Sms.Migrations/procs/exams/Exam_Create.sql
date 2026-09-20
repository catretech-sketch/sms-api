CREATE OR ALTER PROCEDURE dbo.Exam_Create
    @TenantId uniqueidentifier, @Name nvarchar(120), @Type nvarchar(40), @Grades nvarchar(40),
    @FromDate date, @ToDate date, @SubjectCount int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Exams (Id, TenantId, Name, Type, Grades, FromDate, ToDate, SubjectCount)
    VALUES (@Id, @TenantId, @Name, @Type, @Grades, @FromDate, @ToDate, ISNULL(@SubjectCount, 0));

    SELECT Id, TenantId, Name, Type, Grades, FromDate, ToDate, SubjectCount, Status, MarksEnteredPct, Published
    FROM dbo.Exams WHERE Id = @Id;
END
