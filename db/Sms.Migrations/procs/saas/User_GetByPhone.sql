CREATE OR ALTER PROCEDURE dbo.User_GetByPhone
    @Phone nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.Phone = @Phone
    ORDER BY CASE WHEN u.IsPlatform = 1 THEN 0 ELSE 1 END, u.CreatedAt;
END
