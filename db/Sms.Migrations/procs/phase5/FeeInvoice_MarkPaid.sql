CREATE OR ALTER PROCEDURE dbo.FeeInvoice_MarkPaid
    @Id uniqueidentifier, @Method nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FeeInvoices SET Status = 'paid', PaidOn = CAST(SYSUTCDATETIME() AS date), Method = @Method
    WHERE Id = @Id AND Status <> 'paid';

    SELECT Id, TenantId, StudentId, Period, DueDate, Amount, Status, PaidOn, Method
    FROM dbo.FeeInvoices WHERE Id = @Id;
END
