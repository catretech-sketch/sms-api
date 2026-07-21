CREATE OR ALTER PROCEDURE dbo.User_SetStatus
    @UserId uniqueidentifier,
    @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Users SET Status = @Status WHERE Id = @UserId;
END
