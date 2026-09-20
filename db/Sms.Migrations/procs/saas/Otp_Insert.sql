CREATE OR ALTER PROCEDURE dbo.Otp_Insert
    @Identifier nvarchar(256),
    @Channel nvarchar(10),
    @CodeHash varchar(128),
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.OtpCodes (Identifier, Channel, CodeHash, ExpiresAt)
    VALUES (@Identifier, @Channel, @CodeHash, @ExpiresAt);
END
