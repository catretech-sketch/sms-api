CREATE OR ALTER PROCEDURE dbo.Teacher_Create
    @TenantId uniqueidentifier, @Name nvarchar(200), @Gender nvarchar(1), @Department nvarchar(80),
    @Designation nvarchar(80), @SubjectsCsv nvarchar(400), @ClassTeacher nvarchar(40),
    @Phone nvarchar(40), @Email nvarchar(256), @Exp int, @Rating decimal(4,2), @Result decimal(5,2),
    @Load int, @AvatarHue int, @Top bit, @EmployeeCode nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();

    IF @EmployeeCode IS NULL OR LTRIM(RTRIM(@EmployeeCode)) = N''
    BEGIN
        DECLARE @Slug nvarchar(48) =
            (SELECT LOWER(Slug) FROM dbo.Tenants WHERE Id = @TenantId);
        IF @Slug IS NULL OR @Slug = N'' SET @Slug = N'sch';

        DECLARE @Prefix nvarchar(80) = @Slug + N'-TCH-';
        DECLARE @Next int = 1;
        SELECT @Next = ISNULL(MAX(TRY_CAST(SUBSTRING(EmployeeCode, LEN(@Prefix) + 1, 20) AS int)), 0) + 1
        FROM dbo.Teachers
        WHERE TenantId = @TenantId
          AND EmployeeCode LIKE @Prefix + N'%'
          AND TRY_CAST(SUBSTRING(EmployeeCode, LEN(@Prefix) + 1, 20) AS int) IS NOT NULL;

        SET @EmployeeCode = @Prefix + RIGHT(N'0000' + CAST(@Next AS nvarchar(10)), 4);
    END
    ELSE
        SET @EmployeeCode = LOWER(LTRIM(RTRIM(@EmployeeCode)));

    INSERT dbo.Teachers (Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher,
        Phone, Email, Exp, Rating, Result, Load, AvatarHue, [Top], EmployeeCode)
    VALUES (@Id, @TenantId, @Name, @Gender, @Department, @Designation, @SubjectsCsv, @ClassTeacher,
        @Phone, @Email, ISNULL(@Exp, 0), ISNULL(@Rating, 0), ISNULL(@Result, 0), ISNULL(@Load, 0),
        ISNULL(@AvatarHue, 0), ISNULL(@Top, 0), @EmployeeCode);

    UPDATE dbo.Tenants
    SET StaffCount = (
        (SELECT COUNT(*) FROM dbo.Teachers te WHERE te.TenantId = @TenantId AND te.Status = N'active')
      + (SELECT COUNT(*) FROM dbo.Staff st WHERE st.TenantId = @TenantId AND st.Status = N'active')
    )
    WHERE Id = @TenantId;

    SELECT Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher, Phone, Email,
           Exp, Rating, AttendancePct, Result, Load, Status, AvatarHue, [Top], EmployeeCode,
           CAST(NULL AS nvarchar(512)) AS PhotoUrl
    FROM dbo.Teachers WHERE Id = @Id;
END
