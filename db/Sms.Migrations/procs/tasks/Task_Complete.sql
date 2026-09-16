CREATE OR ALTER PROCEDURE dbo.Task_Complete
    @Id uniqueidentifier, @CompletedByUserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tasks
    SET Status = 'completed', CompletedByUserId = @CompletedByUserId, CompletedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

    SELECT Id, TenantId, Title, Detail, Category, AssignedToUserId, AssignedToRoleKey, Priority, Status,
           DueDate, Remarks, PhotoUrl, CreatedByUserId, CompletedByUserId, CreatedAt, CompletedAt
    FROM dbo.Tasks WHERE Id = @Id;
END
