CREATE OR ALTER PROCEDURE dbo.User_SetPassword
    @UserId uniqueidentifier,
    @PasswordHash nvarchar(512)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Users SET PasswordHash = @PasswordHash, Status = 'active' WHERE Id = @UserId;
END
