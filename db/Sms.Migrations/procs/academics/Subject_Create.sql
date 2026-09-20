CREATE OR ALTER PROCEDURE dbo.Subject_Create
    @TenantId uniqueidentifier, @Name nvarchar(80), @Short nvarchar(20),
    @TeacherId uniqueidentifier, @Color nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Subjects (Id, TenantId, Name, Short, TeacherId, Color)
    VALUES (@Id, @TenantId, @Name, @Short, @TeacherId, @Color);

    SELECT Id, TenantId, Name, Short, TeacherId, Color FROM dbo.Subjects WHERE Id = @Id;
END
