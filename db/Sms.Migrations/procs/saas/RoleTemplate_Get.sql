CREATE OR ALTER PROCEDURE dbo.RoleTemplate_Get
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Role, Module, Cap, Effect
    FROM dbo.RoleTemplateOverrides
    WHERE TenantId = @TenantId
    ORDER BY Role, Module, Cap;
END
