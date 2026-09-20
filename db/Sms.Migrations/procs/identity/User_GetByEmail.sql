CREATE OR ALTER PROCEDURE dbo.User_GetByEmail
    @Email nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    -- Prefer platform accounts, then earliest row. Callers that must
    -- password-match across multi-tenant emails should list all peers.
    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword
    FROM dbo.Users u
    WHERE u.Email = @Email
    ORDER BY CASE WHEN u.IsPlatform = 1 THEN 0 ELSE 1 END, u.CreatedAt;
END
