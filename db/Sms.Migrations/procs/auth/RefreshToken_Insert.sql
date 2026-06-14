CREATE OR ALTER PROCEDURE dbo.RefreshToken_Insert
    @UserId uniqueidentifier,
    @TokenHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.RefreshTokens (UserId, TokenHash, ExpiresAt)
    VALUES (@UserId, @TokenHash, @ExpiresAt);
END
