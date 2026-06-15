CREATE OR ALTER PROCEDURE dbo.User_Create
    @TenantId uniqueidentifier,
    @Email nvarchar(256),
    @Phone nvarchar(32),
    @IsPlatform bit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status)
    VALUES (@Id, @TenantId, @Email, @Phone, ISNULL(@IsPlatform, 0), 'active');
    SELECT @Id AS Id;
END
