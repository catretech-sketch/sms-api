CREATE OR ALTER PROCEDURE dbo.PlanUpgradeRequest_AttachRazorpay
    @Id uniqueidentifier,
    @RazorpayOrderId nvarchar(80) = NULL,
    @RazorpayPaymentId nvarchar(80) = NULL,
    @Status nvarchar(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PlanUpgradeRequests SET
        RazorpayOrderId = COALESCE(@RazorpayOrderId, RazorpayOrderId),
        RazorpayPaymentId = COALESCE(@RazorpayPaymentId, RazorpayPaymentId),
        Status = COALESCE(@Status, Status),
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

    EXEC dbo.PlanUpgradeRequest_Get @Id = @Id;
END
