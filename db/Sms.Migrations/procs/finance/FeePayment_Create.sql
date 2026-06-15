CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date])
    VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
        ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date));

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date]
    FROM dbo.FeePayments WHERE Id = @Id;
END
