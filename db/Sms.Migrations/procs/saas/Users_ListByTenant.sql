CREATE OR ALTER PROCEDURE dbo.Users_ListByTenant
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id,
        u.Email,
        u.Phone,
        u.Status,
        u.CreatedAt,
        Roles = ISNULL((
            SELECT STRING_AGG(ur.Role, ',') WITHIN GROUP (ORDER BY ur.Role)
            FROM dbo.UserRoles ur
            WHERE ur.UserId = u.Id
        ), N'')
    FROM dbo.Users u
    WHERE u.TenantId = @TenantId
      AND ISNULL(u.IsPlatform, 0) = 0
    ORDER BY u.Email;
END
