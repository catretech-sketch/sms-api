CREATE OR ALTER PROCEDURE dbo.User_GetByPhone
    @Phone nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.Phone = @Phone
    ORDER BY u.CreatedAt;
END
