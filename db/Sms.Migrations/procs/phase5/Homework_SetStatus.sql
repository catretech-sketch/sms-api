CREATE OR ALTER PROCEDURE dbo.Homework_SetStatus
    @Id uniqueidentifier, @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Homework SET Status = @Status WHERE Id = @Id;

    SELECT Id, TenantId, StudentId, AssignmentId, Title, SubjectId, DueDate, DueTime, Status, Priority, Grade
    FROM dbo.Homework WHERE Id = @Id;
END
