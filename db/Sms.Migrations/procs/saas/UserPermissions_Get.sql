CREATE OR ALTER PROCEDURE dbo.UserPermissions_Get
    @UserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Module, Cap, Effect
    FROM dbo.UserPermissions
    WHERE UserId = @UserId
    ORDER BY Module, Cap;
END
