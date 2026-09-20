CREATE OR ALTER PROCEDURE dbo.Invoice_Create
    @TenantId uniqueidentifier,
    @TenantName nvarchar(200),
    @PlanName nvarchar(100),
    @Amount decimal(18,2),
    @Due datetime2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Invoices (Id, TenantId, TenantName, PlanName, Amount, Status, Issued, Due)
    VALUES (@Id, @TenantId, @TenantName, @PlanName, @Amount, 'open', SYSUTCDATETIME(), @Due);
    SELECT Id, TenantId, TenantName, PlanName, Amount, Status, Issued, Due, PaidOn
    FROM dbo.Invoices WHERE Id = @Id;
END
