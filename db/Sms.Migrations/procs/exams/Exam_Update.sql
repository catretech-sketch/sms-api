CREATE OR ALTER PROCEDURE dbo.Exam_Update
    @Id uniqueidentifier, @Status nvarchar(20), @Published bit, @MarksEnteredPct decimal(5,2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Exams SET
        Status = ISNULL(@Status, Status),
        Published = ISNULL(@Published, Published),
        MarksEnteredPct = ISNULL(@MarksEnteredPct, MarksEnteredPct)
    WHERE Id = @Id;

    SELECT Id, TenantId, Name, Type, Grades, FromDate, ToDate, SubjectCount, Status, MarksEnteredPct, Published
    FROM dbo.Exams WHERE Id = @Id;
END
