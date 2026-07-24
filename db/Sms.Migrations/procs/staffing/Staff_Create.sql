CREATE OR ALTER PROCEDURE dbo.Staff_Create
    @TenantId uniqueidentifier, @Name nvarchar(200), @Gender nvarchar(1), @Role nvarchar(80),
    @Category nvarchar(40), @Department nvarchar(80), @Phone nvarchar(40), @Shift nvarchar(40),
    @Route nvarchar(80), @AvatarHue int, @EmployeeCode nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();

    IF @EmployeeCode IS NULL OR LTRIM(RTRIM(@EmployeeCode)) = N''
    BEGIN
        DECLARE @Slug nvarchar(48) =
            (SELECT LOWER(Slug) FROM dbo.Tenants WHERE Id = @TenantId);
        IF @Slug IS NULL OR @Slug = N'' SET @Slug = N'sch';

        DECLARE @Prefix nvarchar(80) = @Slug + N'-STF-';
        DECLARE @Next int = 1;
        SELECT @Next = ISNULL(MAX(TRY_CAST(SUBSTRING(EmployeeCode, LEN(@Prefix) + 1, 20) AS int)), 0) + 1
        FROM dbo.Staff
        WHERE TenantId = @TenantId
          AND EmployeeCode LIKE @Prefix + N'%'
          AND TRY_CAST(SUBSTRING(EmployeeCode, LEN(@Prefix) + 1, 20) AS int) IS NOT NULL;

        SET @EmployeeCode = @Prefix + RIGHT(N'0000' + CAST(@Next AS nvarchar(10)), 4);
    END
    ELSE
        SET @EmployeeCode = LOWER(LTRIM(RTRIM(@EmployeeCode)));

    INSERT dbo.Staff (Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AvatarHue, EmployeeCode)
    VALUES (@Id, @TenantId, @Name, @Gender, @Role, @Category, @Department, @Phone, @Shift, @Route, ISNULL(@AvatarHue, 0), @EmployeeCode);

    UPDATE dbo.Tenants
    SET StaffCount = (
        (SELECT COUNT(*) FROM dbo.Teachers te WHERE te.TenantId = @TenantId AND te.Status = N'active')
      + (SELECT COUNT(*) FROM dbo.Staff st WHERE st.TenantId = @TenantId AND st.Status = N'active')
    )
    WHERE Id = @TenantId;

    SELECT Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AttendancePct, Status, AvatarHue, EmployeeCode
    FROM dbo.Staff WHERE Id = @Id;
END
