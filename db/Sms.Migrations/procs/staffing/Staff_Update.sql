CREATE OR ALTER PROCEDURE dbo.Staff_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Role nvarchar(80), @Category nvarchar(40),
    @Department nvarchar(80), @Phone nvarchar(40), @Shift nvarchar(40), @Route nvarchar(80), @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Staff SET
        Name = ISNULL(@Name, Name),
        Role = ISNULL(@Role, Role),
        Category = ISNULL(@Category, Category),
        Department = ISNULL(@Department, Department),
        Phone = ISNULL(@Phone, Phone),
        Shift = ISNULL(@Shift, Shift),
        Route = ISNULL(@Route, Route),
        Status = ISNULL(@Status, Status)
    WHERE Id = @Id;

    SELECT Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AttendancePct, Status, AvatarHue
    FROM dbo.Staff WHERE Id = @Id;
END
