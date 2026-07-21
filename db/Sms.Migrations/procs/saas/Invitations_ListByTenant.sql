CREATE OR ALTER PROCEDURE dbo.Invitations_ListByTenant
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, UserId, Email, Phone, RoleLabel, InvitedAt, ExpiresAt, AcceptedAt, RevokedAt, LastResentAt
    FROM dbo.Invitations
    WHERE TenantId = @TenantId
    ORDER BY InvitedAt DESC;
END
