CREATE OR ALTER PROCEDURE dbo.User_GetByStudentId
    @StudentId nvarchar(64),
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status
    FROM dbo.Users u
    WHERE u.StudentId = @StudentId AND u.TenantId = @TenantId;
END
