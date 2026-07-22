CREATE OR ALTER PROCEDURE dbo.RoleTemplate_Set
    @TenantId uniqueidentifier,
    @Json nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.RoleTemplateOverrides WHERE TenantId = @TenantId;

    IF @Json IS NULL OR LTRIM(RTRIM(@Json)) IN (N'', N'[]')
        RETURN;

    INSERT INTO dbo.RoleTemplateOverrides (TenantId, Role, Module, Cap, Effect)
    SELECT @TenantId, j.role, j.module, j.cap, j.effect
    FROM OPENJSON(@Json)
    WITH (
        role nvarchar(32) '$.role',
        module nvarchar(64) '$.module',
        cap char(1) '$.cap',
        effect nvarchar(8) '$.effect'
    ) j
    WHERE j.role IN ('admin', 'principal', 'vice_principal', 'teacher', 'staff')
      AND j.module IS NOT NULL
      AND j.cap IN ('V', 'E', 'A')
      AND j.effect IN ('grant', 'revoke');
END
