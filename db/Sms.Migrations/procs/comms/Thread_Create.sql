CREATE OR ALTER PROCEDURE dbo.Thread_Create
    @TenantId uniqueidentifier, @Name nvarchar(120), @Role nvarchar(40), @IsGroup bit, @ChildId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.ChatThreads (Id, TenantId, Name, Role, IsGroup, ChildId)
    VALUES (@Id, @TenantId, @Name, @Role, ISNULL(@IsGroup, 0), @ChildId);

    SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId
    FROM dbo.ChatThreads WHERE Id = @Id;
END
