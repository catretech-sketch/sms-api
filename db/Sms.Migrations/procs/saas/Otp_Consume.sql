CREATE OR ALTER PROCEDURE dbo.Otp_Consume
    @Identifier nvarchar(256),
    @CodeHash varchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OtpCodes SET ConsumedAt = SYSUTCDATETIME()
    WHERE Identifier = @Identifier AND CodeHash = @CodeHash AND ConsumedAt IS NULL;

    -- Explicit row-count result (not relying on ExecuteNonQuery's affected-row count, which
    -- SET NOCOUNT ON can make unreliable) so the caller can detect a losing race between two
    -- concurrent verify/reset requests and reject the loser instead of both succeeding.
    SELECT @@ROWCOUNT AS RowsAffected;
END
