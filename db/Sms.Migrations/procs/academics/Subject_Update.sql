CREATE OR ALTER PROCEDURE dbo.Subject_Update
    @Id uniqueidentifier,
    @TenantId uniqueidentifier,
    @Name nvarchar(80) = NULL,
    @Short nvarchar(20) = NULL,
    @TeacherId uniqueidentifier = NULL,
    @Color nvarchar(40) = NULL,
    @ClearTeacher bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Subjects
    SET
        Name = COALESCE(@Name, Name),
        Short = COALESCE(@Short, Short),
        Color = COALESCE(@Color, Color),
        TeacherId = CASE
            WHEN @ClearTeacher = 1 THEN NULL
            WHEN @TeacherId IS NOT NULL THEN @TeacherId
            ELSE TeacherId
        END
    WHERE Id = @Id AND TenantId = @TenantId;

    SELECT Id, TenantId, Name, Short, TeacherId, Color
    FROM dbo.Subjects
    WHERE Id = @Id AND TenantId = @TenantId;
END
