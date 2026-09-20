CREATE OR ALTER PROCEDURE dbo.Invitations_MarkAcceptedByUserId
    @UserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Invitations
    SET AcceptedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId AND AcceptedAt IS NULL AND RevokedAt IS NULL;
END
