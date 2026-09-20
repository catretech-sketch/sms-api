CREATE OR ALTER PROCEDURE dbo.Student_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Grade nvarchar(20), @Section nvarchar(20), @Roll int,
    @GuardianName nvarchar(200), @GuardianPhone nvarchar(40), @House nvarchar(40),
    @FeeStatus nvarchar(20), @FeeDue decimal(18,2), @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    -- Tenant isolation is enforced by RLS: a caller's session only sees its own tenant's rows.
    UPDATE dbo.Students SET
        Name = ISNULL(@Name, Name),
        Grade = ISNULL(@Grade, Grade),
        Section = ISNULL(@Section, Section),
        ClassLabel = ISNULL(@Grade, Grade) + '-' + ISNULL(@Section, Section),
        Roll = ISNULL(@Roll, Roll),
        GuardianName = ISNULL(@GuardianName, GuardianName),
        GuardianPhone = ISNULL(@GuardianPhone, GuardianPhone),
        House = ISNULL(@House, House),
        FeeStatus = ISNULL(@FeeStatus, FeeStatus),
        FeeDue = ISNULL(@FeeDue, FeeDue),
        Status = ISNULL(@Status, Status)
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
           GuardianPhone, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address
    FROM dbo.Students WHERE Id = @Id;
END
