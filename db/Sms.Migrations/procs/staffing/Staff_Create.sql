CREATE OR ALTER PROCEDURE dbo.Staff_Create
    @TenantId uniqueidentifier, @Name nvarchar(200), @Gender nvarchar(1), @Role nvarchar(80),
    @Category nvarchar(40), @Department nvarchar(80), @Phone nvarchar(40), @Shift nvarchar(40),
    @Route nvarchar(80), @AvatarHue int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Staff (Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AvatarHue)
    VALUES (@Id, @TenantId, @Name, @Gender, @Role, @Category, @Department, @Phone, @Shift, @Route, ISNULL(@AvatarHue, 0));

    SELECT Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AttendancePct, Status, AvatarHue
    FROM dbo.Staff WHERE Id = @Id;
END
