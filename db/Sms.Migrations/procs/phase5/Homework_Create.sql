CREATE OR ALTER PROCEDURE dbo.Homework_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @AssignmentId uniqueidentifier,
    @Title nvarchar(200), @SubjectId uniqueidentifier, @DueDate date, @DueTime nvarchar(10), @Priority nvarchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Homework (Id, TenantId, StudentId, AssignmentId, Title, SubjectId, DueDate, DueTime, Priority)
    VALUES (@Id, @TenantId, @StudentId, @AssignmentId, @Title, @SubjectId, @DueDate, @DueTime, ISNULL(@Priority, 'med'));

    SELECT Id, TenantId, StudentId, AssignmentId, Title, SubjectId, DueDate, DueTime, Status, Priority, Grade
    FROM dbo.Homework WHERE Id = @Id;
END
