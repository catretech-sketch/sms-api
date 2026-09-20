CREATE OR ALTER PROCEDURE dbo.Payslip_Create
    @TenantId uniqueidentifier, @UserId uniqueidentifier, @Month nvarchar(20), @Year int,
    @Gross decimal(18,2), @Deductions decimal(18,2), @Net decimal(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Payslips (Id, TenantId, UserId, Month, Year, Gross, Deductions, Net)
    VALUES (@Id, @TenantId, @UserId, @Month, ISNULL(@Year, 0), ISNULL(@Gross, 0), ISNULL(@Deductions, 0), ISNULL(@Net, 0));

    SELECT Id, TenantId, UserId, Month, Year, Gross, Deductions, Net, Status FROM dbo.Payslips WHERE Id = @Id;
END
