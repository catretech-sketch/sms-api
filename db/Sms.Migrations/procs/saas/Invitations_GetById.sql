CREATE OR ALTER PROCEDURE dbo.Invitations_GetById
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, UserId, Email, Phone, RoleLabel, InvitedAt, ExpiresAt, AcceptedAt, RevokedAt, LastResentAt
    FROM dbo.Invitations
    WHERE TenantId = @TenantId AND Id = @Id;
END
