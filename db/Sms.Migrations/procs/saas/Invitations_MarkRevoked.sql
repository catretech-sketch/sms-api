CREATE OR ALTER PROCEDURE dbo.Invitations_MarkRevoked
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Invitations
    SET RevokedAt = SYSUTCDATETIME()
    WHERE Id = @Id;
END
