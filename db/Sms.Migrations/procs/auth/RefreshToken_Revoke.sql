CREATE OR ALTER PROCEDURE dbo.RefreshToken_Revoke
    @TokenHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RefreshTokens
    SET RevokedAt = SYSUTCDATETIME()
    WHERE TokenHash = @TokenHash AND RevokedAt IS NULL;
END
