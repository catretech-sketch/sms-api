CREATE OR ALTER PROCEDURE dbo.Otp_GetActive
    @Identifier nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 o.Id, o.CodeHash
    FROM dbo.OtpCodes o
    WHERE o.Identifier = @Identifier AND o.ConsumedAt IS NULL AND o.ExpiresAt > SYSUTCDATETIME()
    ORDER BY o.CreatedAt DESC;
END
