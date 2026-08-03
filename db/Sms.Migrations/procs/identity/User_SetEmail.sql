CREATE OR ALTER PROCEDURE dbo.User_SetEmail
    @UserId uniqueidentifier,
    @Email nvarchar(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Users SET Email = @Email WHERE Id = @UserId;
END
