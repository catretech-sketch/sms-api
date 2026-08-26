CREATE OR ALTER PROCEDURE dbo.Student_RenumberClass
    @TenantId uniqueidentifier,
    @Grade nvarchar(20),
    @Section nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF @TenantId IS NULL RETURN;

    ;WITH ranked AS (
        SELECT Id,
               ROW_NUMBER() OVER (
                   ORDER BY Name ASC, AdmissionNo ASC, Id ASC
               ) AS rn
        FROM dbo.Students
        WHERE TenantId = @TenantId
          AND Status = N'active'
          AND ISNULL(Grade, N'') = ISNULL(@Grade, N'')
          AND ISNULL(Section, N'') = ISNULL(@Section, N'')
    )
    UPDATE s SET Roll = r.rn
    FROM dbo.Students s
    INNER JOIN ranked r ON r.Id = s.Id;
END
