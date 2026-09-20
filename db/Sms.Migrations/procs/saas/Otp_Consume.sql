CREATE OR ALTER PROCEDURE dbo.Otp_Consume
    @Identifier nvarchar(256),
    @CodeHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OtpCodes SET ConsumedAt = SYSUTCDATETIME()
    WHERE Identifier = @Identifier AND CodeHash = @CodeHash AND ConsumedAt IS NULL;
END
