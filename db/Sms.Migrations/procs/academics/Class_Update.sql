CREATE OR ALTER PROCEDURE dbo.Class_Update
    @Id uniqueidentifier,
    @TenantId uniqueidentifier,
    @Name nvarchar(80) = NULL,
    @Grade nvarchar(20) = NULL,
    @Section nvarchar(20) = NULL,
    @Subject nvarchar(80) = NULL,
    @Room nvarchar(40) = NULL,
    @ClassTeacherId uniqueidentifier = NULL,
    @ClearClassTeacher bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Classes
    SET
        Name = COALESCE(@Name, Name),
        Grade = COALESCE(@Grade, Grade),
        Section = COALESCE(@Section, Section),
        Subject = COALESCE(@Subject, Subject),
        Room = COALESCE(@Room, Room),
        ClassTeacherId = CASE
            WHEN @ClearClassTeacher = 1 THEN NULL
            WHEN @ClassTeacherId IS NOT NULL THEN @ClassTeacherId
            ELSE ClassTeacherId
        END
    WHERE Id = @Id AND TenantId = @TenantId;

    SELECT Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId
    FROM dbo.Classes
    WHERE Id = @Id AND TenantId = @TenantId;
END
