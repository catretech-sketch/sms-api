CREATE OR ALTER PROCEDURE dbo.Student_EnsureLogin
    @AdmissionId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    DECLARE @Norm nvarchar(64) = LOWER(LTRIM(RTRIM(@AdmissionId)));
    IF @Norm IS NULL OR @Norm = N'' RETURN;

    DECLARE @TenantId uniqueidentifier,
            @AdmissionNo nvarchar(64),
            @Name nvarchar(200),
            @Email nvarchar(256),
            @Phone nvarchar(32),
            @Status nvarchar(20),
            @UserId uniqueidentifier;

    BEGIN TRAN;

    SELECT TOP 1
        @TenantId = s.TenantId,
        @AdmissionNo = s.AdmissionNo,
        @Name = s.Name,
        @Email = NULLIF(LTRIM(RTRIM(s.Email)), N''),
        @Phone = LEFT(NULLIF(LTRIM(RTRIM(s.GuardianPhone)), N''), 32),
        @Status = s.Status
    FROM dbo.Students s WITH (UPDLOCK, HOLDLOCK)
    WHERE LOWER(LTRIM(RTRIM(s.AdmissionNo))) = @Norm;

    IF @TenantId IS NULL
    BEGIN
        ROLLBACK TRAN;
        RETURN;
    END;

    IF @Status IN (N'inactive', N'removed', N'left', N'withdrawn')
    BEGIN
        ROLLBACK TRAN;
        RETURN;
    END;

    -- Prefer an existing student-role login. Parent rows share StudentId and must not be reused.
    SELECT TOP 1 @UserId = u.Id
    FROM dbo.Users u
    WHERE u.StudentId IS NOT NULL
      AND LOWER(LTRIM(RTRIM(u.StudentId))) = LOWER(LTRIM(RTRIM(@AdmissionNo)))
      AND NOT EXISTS (
            SELECT 1 FROM dbo.UserRoles ur
            WHERE ur.UserId = u.Id
              AND (ur.Role LIKE N'%parent%'
                   OR ur.Role LIKE N'%owner%'
                   OR ur.Role LIKE N'%admin%'
                   OR ur.Role LIKE N'%teacher%'
                   OR ur.Role LIKE N'%principal%'
                   OR ur.Role = N'staff')
      )
      AND (
            EXISTS (
                SELECT 1 FROM dbo.UserRoles ur
                WHERE ur.UserId = u.Id
                  AND (ur.Role = N'student' OR ur.Role LIKE N'%.student')
            )
            OR NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId = u.Id)
      )
    ORDER BY CASE WHEN u.IsPlatform = 1 THEN 0 ELSE 1 END, u.CreatedAt;

    IF @UserId IS NULL
    BEGIN
        IF @Email IS NOT NULL
           AND EXISTS (
                SELECT 1 FROM dbo.Users u
                WHERE u.TenantId = @TenantId
                  AND u.Email IS NOT NULL
                  AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(@Email)
           )
            SET @Email = NULL;

        -- GuardianPhone is often already on a parent login (UX_Users_Tenant_Phone).
        IF @Phone IS NOT NULL
           AND EXISTS (
                SELECT 1 FROM dbo.Users u
                WHERE u.TenantId = @TenantId
                  AND u.Phone IS NOT NULL
                  AND u.Phone = @Phone
           )
            SET @Phone = NULL;

        SET @UserId = NEWID();
        BEGIN TRY
            INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status, StudentId, MustSetPassword, Name)
            VALUES (@UserId, @TenantId, @Email, @Phone, 0, N'active', @AdmissionNo, 1, @Name);
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() NOT IN (2601, 2627)
            BEGIN
                ROLLBACK TRAN;
                THROW;
            END;
            BEGIN TRY
                INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status, StudentId, MustSetPassword, Name)
                VALUES (@UserId, @TenantId, NULL, NULL, 0, N'active', @AdmissionNo, 1, @Name);
            END TRY
            BEGIN CATCH
                ROLLBACK TRAN;
                THROW;
            END CATCH;
        END CATCH;

        IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role = N'student')
            INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, N'student');
    END;

    COMMIT TRAN;

    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword, u.CreatedAt, u.PhotoUrl
    FROM dbo.Users u
    WHERE u.Id = @UserId;
END
