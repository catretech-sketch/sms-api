CREATE OR ALTER PROCEDURE dbo.Otp_GetActive
    @Phone nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 o.Id, o.CodeHash
    FROM dbo.OtpCodes o
    WHERE o.Phone = @Phone AND o.ConsumedAt IS NULL AND o.ExpiresAt > SYSUTCDATETIME()
    ORDER BY o.CreatedAt DESC;
END
