CREATE OR ALTER PROCEDURE dbo.Invoice_MarkPaid
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Invoices SET Status = 'paid', PaidOn = CAST(SYSUTCDATETIME() AS date) WHERE Id = @Id;
    SELECT Id, TenantId, TenantName, PlanName, Amount, Status, Issued, Due, PaidOn
    FROM dbo.Invoices WHERE Id = @Id;
END
