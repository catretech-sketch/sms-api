CREATE OR ALTER PROCEDURE dbo.Student_Create
    @TenantId uniqueidentifier, @AdmissionNo nvarchar(64), @Name nvarchar(200), @Gender nvarchar(1),
    @Grade nvarchar(20), @Section nvarchar(20), @Roll int, @GuardianName nvarchar(200),
    @GuardianPhone nvarchar(40), @House nvarchar(40), @AvatarHue int,
    @Dob datetime2, @Email nvarchar(256), @Address nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @ClassLabel nvarchar(40) =
        CASE WHEN @Grade IS NULL OR @Section IS NULL THEN NULL ELSE @Grade + '-' + @Section END;

    INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll,
        GuardianName, GuardianPhone, House, AvatarHue, Dob, Email, Address)
    VALUES (@Id, @TenantId, @AdmissionNo, @Name, @Gender, @Grade, @Section, @ClassLabel, ISNULL(@Roll, 0),
        @GuardianName, @GuardianPhone, @House, ISNULL(@AvatarHue, 0), @Dob, @Email, @Address);

    UPDATE dbo.Tenants
    SET StudentsCount = (
        SELECT COUNT(*) FROM dbo.Students s WHERE s.TenantId = @TenantId AND s.Status = N'active'
    )
    WHERE Id = @TenantId;

    SELECT Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName,
           GuardianPhone, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address
    FROM dbo.Students WHERE Id = @Id;
END
