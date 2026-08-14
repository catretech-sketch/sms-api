CREATE OR ALTER PROCEDURE dbo.User_ListByAdmissionId
    @AdmissionId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword, u.CreatedAt, u.PhotoUrl
    FROM dbo.Users u
    WHERE u.StudentId IS NOT NULL
      AND LOWER(LTRIM(RTRIM(u.StudentId))) = LOWER(LTRIM(RTRIM(@AdmissionId)))
    ORDER BY CASE WHEN u.IsPlatform = 1 THEN 0 ELSE 1 END, u.CreatedAt;
END
