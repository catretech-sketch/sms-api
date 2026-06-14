CREATE OR ALTER PROCEDURE dbo.User_GetByEmail
    @Email nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.Email = @Email;
END
