CREATE OR ALTER PROCEDURE dbo.Fee_SummaryByTenants
    @TenantIds nvarchar(max),
    @From date,
    @To date
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH ids AS (
        SELECT CAST([value] AS uniqueidentifier) AS TenantId
        FROM OPENJSON(@TenantIds)
    ),
    tenants AS (
        SELECT t.Id, t.Name
        FROM dbo.Tenants t
        INNER JOIN ids i ON i.TenantId = t.Id
    ),
    collected AS (
        SELECT p.TenantId,
               SUM(p.Amount) AS Collected,
               COUNT_BIG(*) AS PaymentCount
        FROM dbo.FeePayments p
        INNER JOIN ids i ON i.TenantId = p.TenantId
        WHERE p.[Date] >= @From AND p.[Date] <= @To
        GROUP BY p.TenantId
    ),
    outstanding AS (
        SELECT inv.TenantId,
               SUM(inv.Amount) AS Outstanding,
               COUNT_BIG(*) AS InvoiceCount
        FROM dbo.FeeInvoices inv
        INNER JOIN ids i ON i.TenantId = inv.TenantId
        WHERE inv.Status <> N'paid'
        GROUP BY inv.TenantId
    )
    SELECT
        t.Id AS TenantId,
        t.Name AS Name,
        CAST(ISNULL(c.Collected, 0) AS decimal(18, 2)) AS Collected,
        CAST(ISNULL(o.Outstanding, 0) AS decimal(18, 2)) AS Outstanding,
        CAST(ISNULL(c.PaymentCount, 0) AS int) AS PaymentCount,
        CAST(ISNULL(o.InvoiceCount, 0) AS int) AS InvoiceCount
    FROM tenants t
    LEFT JOIN collected c ON c.TenantId = t.Id
    LEFT JOIN outstanding o ON o.TenantId = t.Id
    ORDER BY Collected DESC, t.Name;
END
