CREATE OR ALTER PROCEDURE dbo.Teacher_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Department nvarchar(80), @Designation nvarchar(80),
    @SubjectsCsv nvarchar(400), @ClassTeacher nvarchar(40), @Phone nvarchar(40), @Email nvarchar(256),
    @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Teachers SET
        Name = ISNULL(@Name, Name),
        Department = ISNULL(@Department, Department),
        Designation = ISNULL(@Designation, Designation),
        SubjectsCsv = ISNULL(@SubjectsCsv, SubjectsCsv),
        ClassTeacher = ISNULL(@ClassTeacher, ClassTeacher),
        Phone = ISNULL(@Phone, Phone),
        Email = ISNULL(@Email, Email),
        Status = ISNULL(@Status, Status)
    WHERE Id = @Id;

    SELECT Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher, Phone, Email,
           Exp, Rating, AttendancePct, Result, Load, Status, AvatarHue, [Top]
    FROM dbo.Teachers WHERE Id = @Id;
END
