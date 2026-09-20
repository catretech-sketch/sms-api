using FluentMigrator;

namespace Sms.Migrations;

[Migration(175, "FeePayment_Create: idempotent insert via IdempotencyKey + WasCreated flag")]
public sealed class M0175_FeePayment_Create_Idempotent : Migration
{
    public override void Up()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80),
    @InvoiceId uniqueidentifier = NULL, @HeadId nvarchar(64) = NULL, @IdempotencyKey uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @IdempotencyKey IS NOT NULL
    BEGIN
        DECLARE @ExistingId uniqueidentifier = (
            SELECT TOP 1 Id FROM dbo.FeePayments
            WHERE TenantId = @TenantId AND IdempotencyKey = @IdempotencyKey);
        IF @ExistingId IS NOT NULL
        BEGIN
            SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
                   CAST(0 AS bit) AS WasCreated
            FROM dbo.FeePayments WHERE Id = @ExistingId;
            RETURN;
        END
    END

    DECLARE @Id uniqueidentifier = NEWID();
    BEGIN TRY
        INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId, IdempotencyKey, CreatedAt)
        VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
            ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date), @InvoiceId, @HeadId, @IdempotencyKey, SYSUTCDATETIME());
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2601, 2627) AND @IdempotencyKey IS NOT NULL
        BEGIN
            SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
                   CAST(0 AS bit) AS WasCreated
            FROM dbo.FeePayments WHERE TenantId = @TenantId AND IdempotencyKey = @IdempotencyKey;
            RETURN;
        END
        ELSE
            THROW;
    END CATCH

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId,
           CAST(1 AS bit) AS WasCreated
    FROM dbo.FeePayments WHERE Id = @Id;
END
""");
    }

    public override void Down()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80),
    @InvoiceId uniqueidentifier = NULL, @HeadId nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId)
    VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
        ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date), @InvoiceId, @HeadId);

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
    FROM dbo.FeePayments WHERE Id = @Id;
END
""");
    }
}
