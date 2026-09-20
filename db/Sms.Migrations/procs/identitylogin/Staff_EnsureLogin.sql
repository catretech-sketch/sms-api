CREATE OR ALTER PROCEDURE dbo.Staff_EnsureLogin
    @Email nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    DECLARE @Norm nvarchar(256) = LOWER(LTRIM(RTRIM(@Email)));
    IF @Norm IS NULL OR @Norm = N'' RETURN;

    DECLARE @StaffId uniqueidentifier,
            @TenantId uniqueidentifier,
            @Name nvarchar(200),
            @Status nvarchar(20),
            @ExistingUserId uniqueidentifier,
            @UserId uniqueidentifier;

    BEGIN TRAN;

    SELECT TOP 1
        @StaffId = s.Id,
        @TenantId = s.TenantId,
        @Name = s.Name,
        @Status = s.Status,
        @ExistingUserId = s.UserId
    FROM dbo.Staff s WITH (UPDLOCK, HOLDLOCK)
    WHERE s.Email IS NOT NULL
      AND LOWER(LTRIM(RTRIM(s.Email))) = @Norm
    ORDER BY s.CreatedAt;

    IF @StaffId IS NULL
    BEGIN
        ROLLBACK TRAN;
        RETURN;
    END;

    IF @Status = N'inactive'
    BEGIN
        ROLLBACK TRAN;
        RETURN;
    END;

    IF @ExistingUserId IS NOT NULL
        SET @UserId = @ExistingUserId;
    ELSE
    BEGIN
        -- The email may already belong to a login in this tenant (e.g. a CRM
        -- account) — link that one instead of creating a duplicate.
        SELECT TOP 1 @UserId = u.Id
        FROM dbo.Users u
        WHERE u.TenantId = @TenantId
          AND u.Email IS NOT NULL
          AND LOWER(LTRIM(RTRIM(u.Email))) = @Norm
        ORDER BY u.CreatedAt;

        IF @UserId IS NULL
        BEGIN
            SET @UserId = NEWID();
            INSERT dbo.Users (Id, TenantId, Email, Phone, IsPlatform, Status, StudentId, MustSetPassword, Name)
            VALUES (@UserId, @TenantId, @Norm, NULL, 0, N'active', NULL, 1, @Name);
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND Role = N'staff')
            INSERT dbo.UserRoles (UserId, Role) VALUES (@UserId, N'staff');

        UPDATE dbo.Staff SET UserId = @UserId WHERE Id = @StaffId;
    END;

    COMMIT TRAN;

    SELECT TOP 1 u.Id, u.TenantId, u.Email, u.StudentId, u.Phone,
           u.PasswordHash, u.IsPlatform, u.Status, u.Name, u.MustSetPassword, u.CreatedAt, u.PhotoUrl
    FROM dbo.Users u
    WHERE u.Id = @UserId;
END
