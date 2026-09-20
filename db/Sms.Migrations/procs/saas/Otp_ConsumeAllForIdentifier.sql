CREATE OR ALTER PROCEDURE dbo.Otp_ConsumeAllForIdentifier
    @Identifier nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OtpCodes SET ConsumedAt = SYSUTCDATETIME()
    WHERE Identifier = @Identifier AND ConsumedAt IS NULL;
END
