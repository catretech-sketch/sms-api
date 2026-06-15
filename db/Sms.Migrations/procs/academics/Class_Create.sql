CREATE OR ALTER PROCEDURE dbo.Class_Create
    @TenantId uniqueidentifier, @Name nvarchar(80), @Grade nvarchar(20), @Section nvarchar(20),
    @Subject nvarchar(80), @Room nvarchar(40), @ClassTeacherId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Classes (Id, TenantId, Name, Grade, Section, Subject, Room, ClassTeacherId)
    VALUES (@Id, @TenantId, @Name, @Grade, @Section, @Subject, @Room, @ClassTeacherId);

    SELECT Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId
    FROM dbo.Classes WHERE Id = @Id;
END
