CREATE OR ALTER PROCEDURE dbo.Student_GetByAdmissionNo
    @AdmissionId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 s.Id, s.TenantId, s.AdmissionNo, s.Name, s.Email, s.GuardianPhone, s.Status, s.GuardianEmail
    FROM dbo.Students s
    WHERE LOWER(LTRIM(RTRIM(s.AdmissionNo))) = LOWER(LTRIM(RTRIM(@AdmissionId)));
END
