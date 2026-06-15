CREATE OR ALTER PROCEDURE dbo.Teacher_Create
    @TenantId uniqueidentifier, @Name nvarchar(200), @Gender nvarchar(1), @Department nvarchar(80),
    @Designation nvarchar(80), @SubjectsCsv nvarchar(400), @ClassTeacher nvarchar(40),
    @Phone nvarchar(40), @Email nvarchar(256), @Exp int, @Rating decimal(4,2), @Result decimal(5,2),
    @Load int, @AvatarHue int, @Top bit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Teachers (Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher,
        Phone, Email, Exp, Rating, Result, Load, AvatarHue, [Top])
    VALUES (@Id, @TenantId, @Name, @Gender, @Department, @Designation, @SubjectsCsv, @ClassTeacher,
        @Phone, @Email, ISNULL(@Exp, 0), ISNULL(@Rating, 0), ISNULL(@Result, 0), ISNULL(@Load, 0),
        ISNULL(@AvatarHue, 0), ISNULL(@Top, 0));

    SELECT Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher, Phone, Email,
           Exp, Rating, AttendancePct, Result, Load, Status, AvatarHue, [Top]
    FROM dbo.Teachers WHERE Id = @Id;
END
