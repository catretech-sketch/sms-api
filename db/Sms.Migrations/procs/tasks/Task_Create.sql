CREATE OR ALTER PROCEDURE dbo.Task_Create
    @TenantId uniqueidentifier, @CreatedByUserId uniqueidentifier, @Title nvarchar(200),
    @Detail nvarchar(2000) = NULL, @Category nvarchar(40) = NULL,
    @AssignedToUserId uniqueidentifier = NULL, @AssignedToRoleKey nvarchar(20) = NULL,
    @Priority nvarchar(10), @DueDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Tasks
        (Id, TenantId, Title, Detail, Category, AssignedToUserId, AssignedToRoleKey, Priority, DueDate, CreatedByUserId)
    VALUES
        (@Id, @TenantId, @Title, @Detail, @Category, @AssignedToUserId, @AssignedToRoleKey, @Priority, @DueDate, @CreatedByUserId);

    SELECT Id, TenantId, Title, Detail, Category, AssignedToUserId, AssignedToRoleKey, Priority, Status,
           DueDate, Remarks, PhotoUrl, CreatedByUserId, CompletedByUserId, CreatedAt, CompletedAt
    FROM dbo.Tasks WHERE Id = @Id;
END
