CREATE OR ALTER PROCEDURE dbo.Parent_EnsureLogin
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
        @Name = NULLIF(LTRIM(RTRIM(s.GuardianName)), N''),
        @Email = NULLIF(LTRIM(RTRIM(s.GuardianEmail)), N''),
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

    IF @Email IS NULL AND @Phone IS NULL
    BEGIN
        ROLLBACK TRAN;
        RETURN;
    END;

    -- Existing parent-role login for this admission.
    SELECT TOP 1 @UserId = u.Id
    FROM dbo.Users u
    WHERE u.StudentId IS NOT NULL
      AND LOWER(LTRIM(RTRIM(u.StudentId))) = LOWER(LTRIM(RTRIM(@AdmissionNo)))
      AND EXISTS (
            SELECT 1 FROM dbo.UserRoles ur
            WHERE ur.UserId = u.Id AND ur.Role LIKE N'%parent%'
      )
    ORDER BY u.CreatedAt;

    -- Same guardian email already has a parent login (sibling wards).
    IF @UserId IS NULL AND @Email IS NOT NULL
        SELECT TOP 1 @UserId = u.Id
        FROM dbo.Users u
        INNER JOIN dbo.UserRoles ur ON ur.UserId = u.Id AND ur.Role LIKE N'%parent%'
        WHERE u.TenantId = @TenantId
          AND u.Email IS NOT NULL
          AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(@Email)
        ORDER BY u.CreatedAt;

    IF @UserId IS NULL
    BEGIN
        -- Do not collide with an existing student/staff login that already owns this email.
        IF @Email IS NOT NULL
           AND EXISTS (
                SELECT 1 FROM dbo.Users u
                WHERE u.TenantId = @TenantId
                  AND u.Email IS NOT NULL
                  AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(@Email)
           )
            SET @Email = NULL;

        -- Guardian phone is often copied onto the student login — still create parent by email.
        IF @Phone IS NOT NULL
           AND EXISTS (
                SELECT 1 FROM dbo.Users u
                WHERE u.TenantId = @TenantId
                  AND u.Phone IS NOT NULL
                  AND u.Phone = @Phone
           )
            SET @Phone = NULL;

        IF @Email IS NULL AND @Phone IS NULL
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END;

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
            -- Student login often already owns GuardianPhone (UX_Users_Tenant_Phone).
            -- Keep the parent row keyed by email.
            SET @Phone = NULL;
            IF @Email IS NULL
            BEGIN
                ROLLBACK TRAN;
                RETURN;
            END;
            BEGIN TRY
                INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status, StudentId, MustSetPassword, Name)
                VALUES (@UserId, @TenantId, @Email, NULL, 0, N'active', @AdmissionNo, 1, @Name);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2601, 2627)
                BEGIN
                    ROLLBACK TRAN;
                    THROW;
                END;
                ROLLBACK TRAN;
                RETURN;
            END CATCH;
        END CATCH;

        IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role = N'student.parent')
            INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, N'student.parent');
    END
    ELSE
    BEGIN
        IF @Email IS NOT NULL
            UPDATE dbo.Users SET Email = @Email
            WHERE Id = @UserId
              AND (Email IS NULL OR LOWER(LTRIM(RTRIM(Email))) <> LOWER(@Email))
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.Users x
                    WHERE x.TenantId = @TenantId AND x.Id <> @UserId
                      AND x.Email IS NOT NULL
                      AND LOWER(LTRIM(RTRIM(x.Email))) = LOWER(@Email)
              );
        IF @Phone IS NOT NULL
            UPDATE dbo.Users SET Phone = @Phone
            WHERE Id = @UserId AND Phone IS NULL
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.Users x
                    WHERE x.TenantId = @TenantId AND x.Id <> @UserId
                      AND x.Phone IS NOT NULL AND x.Phone = @Phone
              );
        IF @Name IS NOT NULL
            UPDATE dbo.Users SET Name = @Name WHERE Id = @UserId AND (Name IS NULL OR LTRIM(RTRIM(Name)) = N'');
        IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role LIKE N'%parent%')
            INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, N'student.parent');
    END;

    -- Multi-child roster. Idempotent; login must not fail if the row already exists.
    IF OBJECT_ID(N'dbo.ParentStudentLinks', N'U') IS NOT NULL
    BEGIN
        DECLARE @StudentGuid uniqueidentifier;
        SELECT @StudentGuid = s.Id
        FROM dbo.Students s
        WHERE s.TenantId = @TenantId
          AND LOWER(LTRIM(RTRIM(s.AdmissionNo))) = LOWER(LTRIM(RTRIM(@AdmissionNo)));

        IF @StudentGuid IS NOT NULL
        BEGIN
            BEGIN TRY
                IF NOT EXISTS (
                    SELECT 1 FROM dbo.ParentStudentLinks
                    WHERE ParentUserId = @UserId AND StudentId = @StudentGuid
                )
                    INSERT dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId)
                    VALUES (@UserId, @StudentGuid, @TenantId);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2601, 2627)
                BEGIN
                    ROLLBACK TRAN;
                    THROW;
                END;
            END CATCH;
        END;
    END;

    COMMIT TRAN;

    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword, u.CreatedAt, u.PhotoUrl
    FROM dbo.Users u
    WHERE u.Id = @UserId;
END
