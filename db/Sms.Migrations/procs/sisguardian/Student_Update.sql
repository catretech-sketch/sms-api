CREATE OR ALTER PROCEDURE dbo.Student_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Grade nvarchar(20), @Section nvarchar(20), @Roll int,
    @GuardianName nvarchar(200), @GuardianPhone nvarchar(40), @GuardianEmail nvarchar(256), @House nvarchar(40),
    @FeeStatus nvarchar(20), @FeeDue decimal(18,2), @Status nvarchar(20), @PhotoUrl nvarchar(max) = NULL,
    @SetPhoto bit = 0, @Gender nvarchar(1) = NULL, @Dob datetime2 = NULL, @Email nvarchar(256) = NULL,
    @Address nvarchar(500) = NULL, @AvatarHue int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Students SET
        Name = ISNULL(@Name, Name),
        Grade = ISNULL(@Grade, Grade),
        Section = ISNULL(@Section, Section),
        ClassLabel = ISNULL(@Grade, Grade) + '-' + ISNULL(@Section, Section),
        Roll = ISNULL(@Roll, Roll),
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

    DECLARE @TenantId uniqueidentifier =
        (SELECT TOP 1 TenantId FROM dbo.Students WHERE Id = @Id);
    IF @TenantId IS NOT NULL
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
