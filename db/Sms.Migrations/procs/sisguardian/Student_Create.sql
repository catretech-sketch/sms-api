CREATE OR ALTER PROCEDURE dbo.Student_Create
    @TenantId uniqueidentifier, @AdmissionNo nvarchar(64), @Name nvarchar(200), @Gender nvarchar(1),
    @Grade nvarchar(20), @Section nvarchar(20), @Roll int, @GuardianName nvarchar(200),
    @GuardianPhone nvarchar(40), @GuardianEmail nvarchar(256), @House nvarchar(40), @AvatarHue int,
    @Dob datetime2, @Email nvarchar(256), @Address nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @ClassLabel nvarchar(40) =
        CASE WHEN @Grade IS NULL OR @Section IS NULL THEN NULL ELSE @Grade + '-' + @Section END;

    IF @AdmissionNo IS NULL OR LTRIM(RTRIM(@AdmissionNo)) = N''
    BEGIN
        DECLARE @Slug nvarchar(48) =
            (SELECT LOWER(REPLACE(Slug, N'-', N'')) FROM dbo.Tenants WHERE Id = @TenantId);
        IF @Slug IS NULL OR @Slug = N'' SET @Slug = N'sch';

        DECLARE @Year nvarchar(2) = RIGHT(CAST(YEAR(SYSUTCDATETIME()) AS nvarchar(4)), 2);
        DECLARE @Prefix nvarchar(80) = @Slug + N'/STU/' + @Year + N'/';
        DECLARE @Next int = 1;
        SELECT @Next = ISNULL(MAX(TRY_CAST(SUBSTRING(AdmissionNo, LEN(@Prefix) + 1, 20) AS int)), 0) + 1
        FROM dbo.Students
        WHERE TenantId = @TenantId
          AND AdmissionNo LIKE @Prefix + N'%'
          AND TRY_CAST(SUBSTRING(AdmissionNo, LEN(@Prefix) + 1, 20) AS int) IS NOT NULL;

        SET @AdmissionNo = @Prefix + RIGHT(N'0000' + CAST(@Next AS nvarchar(10)), 4);
    END
    ELSE
        SET @AdmissionNo = LTRIM(RTRIM(@AdmissionNo));

    INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll,
        GuardianName, GuardianPhone, GuardianEmail, House, AvatarHue, Dob, Email, Address)
    VALUES (@Id, @TenantId, @AdmissionNo, @Name, @Gender, @Grade, @Section, @ClassLabel, ISNULL(@Roll, 0),
        @GuardianName, @GuardianPhone, @GuardianEmail, @House, ISNULL(@AvatarHue, 0), @Dob, @Email, @Address);

    UPDATE dbo.Tenants
    SET StudentsCount = (
        SELECT COUNT(*) FROM dbo.Students s WHERE s.TenantId = @TenantId AND s.Status = N'active'
    )
    WHERE Id = @TenantId;

    SELECT Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName,
           GuardianPhone, GuardianEmail, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address,
           PhotoUrl
    FROM dbo.Students WHERE Id = @Id;
END
