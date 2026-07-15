CREATE OR ALTER PROCEDURE dbo.PlanUpgradeRequest_GetByOrder
    @RazorpayOrderId nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        r.Id, r.TenantId, t.Name AS TenantName,
        r.FromPlanId, fp.Name AS FromPlanName, fp.Tier AS FromTier,
        r.ToPlanId, tp.Name AS ToPlanName, tp.Tier AS ToTier,
        r.Amount, r.Currency, r.Mode, r.Status, r.InvoiceId,
        r.RazorpayOrderId, r.RazorpayPaymentId,
        r.RequestedByUserId, r.ReviewedByUserId, r.Notes,
        r.CreatedAt, r.UpdatedAt
    FROM dbo.PlanUpgradeRequests r
    JOIN dbo.Tenants t ON t.Id = r.TenantId
    LEFT JOIN dbo.Plans fp ON fp.Id = r.FromPlanId
    JOIN dbo.Plans tp ON tp.Id = r.ToPlanId
    WHERE r.RazorpayOrderId = @RazorpayOrderId;
END
