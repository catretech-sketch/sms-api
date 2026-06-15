CREATE OR ALTER PROCEDURE dbo.Users_BulkCreate
    @TenantId uniqueidentifier,
    @Rows dbo.UsersTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @created int = 0, @skipped int = 0;

    -- Stage rows with a generated id; skip rows whose email/phone already exists in the tenant.
    DECLARE @New TABLE (Id uniqueidentifier, Email nvarchar(256), Phone nvarchar(32), Role nvarchar(64));
    INSERT @New (Id, Email, Phone, Role)
    SELECT NEWID(), r.Email, r.Phone, r.Role
    FROM @Rows r
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Users u
        WHERE u.TenantId = @TenantId
          AND ((r.Email IS NOT NULL AND u.Email = r.Email)
            OR (r.Phone IS NOT NULL AND u.Phone = r.Phone)));

    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status)
    SELECT Id, @TenantId, Email, Phone, 0, 'active' FROM @New;
    SET @created = @@ROWCOUNT;

    INSERT dbo.UserRoles (UserId, Role)
    SELECT Id, Role FROM @New WHERE Role IS NOT NULL;

    SELECT @created AS Created,
           (SELECT COUNT(*) FROM @Rows) - @created AS Skipped;
END
