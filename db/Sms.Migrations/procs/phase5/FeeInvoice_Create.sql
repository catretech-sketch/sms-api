CREATE OR ALTER PROCEDURE dbo.FeeInvoice_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @Period nvarchar(60), @DueDate date, @Amount decimal(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeeInvoices (Id, TenantId, StudentId, Period, DueDate, Amount)
    VALUES (@Id, @TenantId, @StudentId, @Period, @DueDate, ISNULL(@Amount, 0));

    SELECT Id, TenantId, StudentId, Period, DueDate, Amount, Status, PaidOn, Method
    FROM dbo.FeeInvoices WHERE Id = @Id;
END
