CREATE OR ALTER PROCEDURE dbo.Student_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Grade nvarchar(20), @Section nvarchar(20), @Roll int,
    @GuardianName nvarchar(200), @GuardianPhone nvarchar(40), @GuardianEmail nvarchar(256), @House nvarchar(40),
    @FeeStatus nvarchar(20), @FeeDue decimal(18,2), @Status nvarchar(20), @PhotoUrl nvarchar(max) = NULL,
    @SetPhoto bit = 0, @Gender nvarchar(1) = NULL, @Dob datetime2 = NULL, @Email nvarchar(256) = NULL,
    @Address nvarchar(500) = NULL, @AvatarHue int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantId uniqueidentifier, @OldGrade nvarchar(20), @OldSection nvarchar(20);
    SELECT @TenantId = TenantId, @OldGrade = Grade, @OldSection = Section
    FROM dbo.Students WHERE Id = @Id;

    UPDATE dbo.Students SET
        Name = ISNULL(@Name, Name),
        Grade = ISNULL(@Grade, Grade),
        Section = ISNULL(@Section, Section),
        ClassLabel = ISNULL(@Grade, Grade) + '-' + ISNULL(@Section, Section),
        GuardianName = ISNULL(@GuardianName, GuardianName),
        GuardianPhone = ISNULL(@GuardianPhone, GuardianPhone),
        GuardianEmail = ISNULL(@GuardianEmail, GuardianEmail),
        House = ISNULL(@House, House),
        FeeStatus = ISNULL(@FeeStatus, FeeStatus),
        FeeDue = ISNULL(@FeeDue, FeeDue),
        Status = ISNULL(@Status, Status),
        PhotoUrl = CASE WHEN @SetPhoto = 1 THEN @PhotoUrl ELSE PhotoUrl END,
        Gender = ISNULL(@Gender, Gender),
        Dob = ISNULL(@Dob, Dob),
        Email = ISNULL(@Email, Email),
        Address = ISNULL(@Address, Address),
        AvatarHue = ISNULL(@AvatarHue, AvatarHue)
    WHERE Id = @Id;

    UPDATE dbo.Students SET Roll = 0 WHERE Id = @Id AND Status <> N'active';

    IF @TenantId IS NOT NULL
    BEGIN
        DECLARE @NewGrade nvarchar(20), @NewSection nvarchar(20);
        SELECT @NewGrade = Grade, @NewSection = Section FROM dbo.Students WHERE Id = @Id;

        EXEC dbo.Student_RenumberClass @TenantId = @TenantId, @Grade = @OldGrade, @Section = @OldSection;
        IF ISNULL(@NewGrade, N'') <> ISNULL(@OldGrade, N'')
           OR ISNULL(@NewSection, N'') <> ISNULL(@OldSection, N'')
            EXEC dbo.Student_RenumberClass @TenantId = @TenantId, @Grade = @NewGrade, @Section = @NewSection;

        UPDATE dbo.Tenants
        SET StudentsCount = (
            SELECT COUNT(*) FROM dbo.Students s WHERE s.TenantId = @TenantId AND s.Status = N'active'
        )
        WHERE Id = @TenantId;
    END

    SELECT Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName,
           GuardianPhone, GuardianEmail, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address,
           PhotoUrl
    FROM dbo.Students WHERE Id = @Id;
END
