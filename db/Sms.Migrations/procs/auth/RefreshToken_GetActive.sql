CREATE OR ALTER PROCEDURE dbo.RefreshToken_GetActive
    @TokenHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rt.UserId
    FROM dbo.RefreshTokens rt
    WHERE rt.TokenHash = @TokenHash
      AND rt.RevokedAt IS NULL
      AND rt.ExpiresAt > SYSUTCDATETIME();
END
