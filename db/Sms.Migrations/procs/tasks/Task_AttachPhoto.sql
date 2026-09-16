CREATE OR ALTER PROCEDURE dbo.Task_AttachPhoto
    @Id uniqueidentifier, @PhotoUrl nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tasks SET PhotoUrl = @PhotoUrl WHERE Id = @Id;

    SELECT Id, TenantId, Title, Detail, Category, AssignedToUserId, AssignedToRoleKey, Priority, Status,
           DueDate, Remarks, PhotoUrl, CreatedByUserId, CompletedByUserId, CreatedAt, CompletedAt
    FROM dbo.Tasks WHERE Id = @Id;
END
