CREATE OR ALTER PROCEDURE dbo.Invitations_Create
    @TenantId uniqueidentifier,
    @UserId uniqueidentifier,
    @Email nvarchar(256),
    @Phone nvarchar(32),
    @RoleLabel nvarchar(64),
    @InvitedByUserId uniqueidentifier,
    @ExpiresAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Invitations (Id, TenantId, UserId, Email, Phone, RoleLabel, InvitedByUserId, ExpiresAt)
    VALUES (@Id, @TenantId, @UserId, @Email, @Phone, @RoleLabel, @InvitedByUserId, @ExpiresAt);
    SELECT @Id AS Id;
END
