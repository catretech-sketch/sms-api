CREATE OR ALTER PROCEDURE dbo.Otp_Insert
    @Phone nvarchar(32),
    @CodeHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.OtpCodes (Phone, CodeHash, ExpiresAt)
    VALUES (@Phone, @CodeHash, @ExpiresAt);
END
