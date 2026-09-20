CREATE OR ALTER PROCEDURE dbo.Users_BulkCreate
    @TenantId uniqueidentifier,
    @Rows dbo.UsersTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @created int = 0, @skipped int = 0;

    -- Stage rows with a generated id; skip rows whose email/phone already exists in the tenant.
    -- De-duplicate within the incoming batch first (keep the first occurrence of each email/phone).
    DECLARE @New TABLE (Id uniqueidentifier, Email nvarchar(256), Phone nvarchar(32), Role nvarchar(64));
    ;WITH dedup AS (
        SELECT r.Email, r.Phone, r.Role,
               ROW_NUMBER() OVER (
                   PARTITION BY COALESCE(r.Email, '') , COALESCE(r.Phone, '')
                   ORDER BY (SELECT 1)) AS rn
        FROM @Rows r)
    INSERT @New (Id, Email, Phone, Role)
    SELECT NEWID(), d.Email, d.Phone, d.Role
    FROM dedup d
    WHERE d.rn = 1
      AND NOT EXISTS (
        SELECT 1 FROM dbo.Users u
        WHERE u.TenantId = @TenantId
          AND ((d.Email IS NOT NULL AND u.Email = d.Email)
            OR (d.Phone IS NOT NULL AND u.Phone = d.Phone)));

    INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status)
    SELECT Id, @TenantId, Email, Phone, 0, 'active' FROM @New;
    SET @created = @@ROWCOUNT;

    INSERT dbo.UserRoles (UserId, Role)
    SELECT Id, Role FROM @New WHERE Role IS NOT NULL;

    SELECT @created AS Created,
           (SELECT COUNT(*) FROM @Rows) - @created AS Skipped;
END
