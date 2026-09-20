CREATE OR ALTER PROCEDURE dbo.UserRoles_Replace
    @UserId uniqueidentifier,
    @Roles nvarchar(max) -- comma-separated role names
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.UserRoles WHERE UserId = @UserId;

    INSERT INTO dbo.UserRoles (UserId, Role)
    SELECT DISTINCT @UserId, LTRIM(RTRIM(value))
    FROM STRING_SPLIT(@Roles, ',')
    WHERE LTRIM(RTRIM(value)) <> N'';
END
