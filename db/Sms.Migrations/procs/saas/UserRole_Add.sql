CREATE OR ALTER PROCEDURE dbo.UserRole_Add
    @UserId uniqueidentifier,
    @Role nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role = @Role)
        INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, @Role);
END
