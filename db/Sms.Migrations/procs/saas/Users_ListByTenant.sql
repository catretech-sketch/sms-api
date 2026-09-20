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
      AND u.Status <> 'removed'
      -- This screen manages CRM/staff access, not the whole tenant roster — students and
      -- parents have Users rows too (for their own app logins) but never a CRM console role,
      -- so without this filter they show up here mislabeled (the frontend defaults any
      -- unrecognized role to "Teacher" for display).
      AND EXISTS (
          SELECT 1 FROM dbo.UserRoles ur
          WHERE ur.UserId = u.Id AND (ur.Role LIKE N'school.%' OR ur.Role = N'staff')
      )
    ORDER BY u.Email;
END
