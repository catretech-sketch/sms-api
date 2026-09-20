CREATE OR ALTER PROCEDURE dbo.Tenant_GetTierAndStatus
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.Tier, t.Status FROM dbo.Tenants t WHERE t.Id = @TenantId;
END
