CREATE OR ALTER PROCEDURE dbo.Subject_Delete
    @Id uniqueidentifier, @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Subjects WHERE Id = @Id AND TenantId = @TenantId;
END
