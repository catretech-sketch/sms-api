CREATE OR ALTER PROCEDURE dbo.Invoice_Refund
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Invoices SET Status = 'open', PaidOn = NULL WHERE Id = @Id AND Status = 'paid';
    SELECT Id, TenantId, TenantName, PlanName, Amount, Status, Issued, Due, PaidOn
    FROM dbo.Invoices WHERE Id = @Id;
END
