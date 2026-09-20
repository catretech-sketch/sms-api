CREATE OR ALTER PROCEDURE dbo.User_SetPhoto
    @UserId uniqueidentifier,
    @PhotoUrl nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Users SET PhotoUrl = @PhotoUrl WHERE Id = @UserId;
END
