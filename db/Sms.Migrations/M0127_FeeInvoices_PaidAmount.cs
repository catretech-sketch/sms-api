using FluentMigrator;

namespace Sms.Migrations;

[Migration(127, "FeeInvoices.PaidAmount for partial collection (accumulate Record payment)")]
public sealed class M0127_FeeInvoices_PaidAmount : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF COL_LENGTH('dbo.FeeInvoices', 'PaidAmount') IS NULL
    ALTER TABLE dbo.FeeInvoices ADD PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_FeeInvoices_PaidAmount DEFAULT (0);
""");

        /* Reconcile PaidAmount from FeePayments so wrongly full-paid rows reopen as partial. */
        Execute.Sql("""
;WITH pay AS (
    SELECT StudentId, CAST(SUM(Amount) AS decimal(18,2)) AS Paid
    FROM dbo.FeePayments
    GROUP BY StudentId
)
UPDATE i
SET
    PaidAmount = CASE
        WHEN ISNULL(p.Paid, 0) > i.Amount THEN i.Amount
        ELSE ISNULL(p.Paid, 0)
    END,
    Status = CASE
        WHEN ISNULL(p.Paid, 0) >= i.Amount AND i.Amount > 0 THEN N'paid'
        WHEN ISNULL(p.Paid, 0) > 0 THEN N'partial'
        ELSE N'due'
    END,
    PaidOn = CASE
        WHEN ISNULL(p.Paid, 0) >= i.Amount AND i.Amount > 0 THEN ISNULL(i.PaidOn, CAST(SYSUTCDATETIME() AS date))
        ELSE NULL
    END
FROM dbo.FeeInvoices i
LEFT JOIN pay p ON p.StudentId = i.StudentId;
""");
    }

    public override void Down()
    {
        Execute.Sql("""
IF COL_LENGTH('dbo.FeeInvoices', 'PaidAmount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FeeInvoices DROP CONSTRAINT IF EXISTS DF_FeeInvoices_PaidAmount;
    ALTER TABLE dbo.FeeInvoices DROP COLUMN PaidAmount;
END
""");
    }
}
