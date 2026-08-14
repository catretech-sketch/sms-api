CREATE OR ALTER PROCEDURE dbo.User_Create
    @TenantId uniqueidentifier,
    @Email nvarchar(256),
    @Phone nvarchar(32),
    @IsPlatform bit,
    @StudentId nvarchar(64) = NULL,
    @MustSetPassword bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status, StudentId, MustSetPassword)
    VALUES (@Id, @TenantId, @Email, @Phone, ISNULL(@IsPlatform, 0), 'active', @StudentId, ISNULL(@MustSetPassword, 0));
    SELECT @Id AS Id;
END
