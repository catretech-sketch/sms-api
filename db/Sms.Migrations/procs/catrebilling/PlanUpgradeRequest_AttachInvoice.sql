CREATE OR ALTER PROCEDURE dbo.PlanUpgradeRequest_AttachInvoice
    @Id uniqueidentifier,
    @InvoiceId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PlanUpgradeRequests SET
        InvoiceId = @InvoiceId,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

    EXEC dbo.PlanUpgradeRequest_Get @Id = @Id;
END
