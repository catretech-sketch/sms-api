CREATE OR ALTER PROCEDURE dbo.Invitations_MarkResent
    @Id uniqueidentifier,
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Invitations
    SET ExpiresAt = @ExpiresAt, LastResentAt = SYSUTCDATETIME()
    WHERE Id = @Id;
END
