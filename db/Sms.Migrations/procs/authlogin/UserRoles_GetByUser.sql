CREATE OR ALTER PROCEDURE dbo.UserRoles_GetByUser
    @UserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ur.Role
    FROM dbo.UserRoles ur
    WHERE ur.UserId = @UserId
    ORDER BY ur.Role;
END
