CREATE OR ALTER PROCEDURE dbo.Report_Revenue
AS
BEGIN
    SET NOCOUNT ON;

    -- Net growth + churn from the two latest snapshots.
    DECLARE @currActive int, @currCancel int, @prevActive int, @prevCancel int;
    SELECT TOP 1 @currActive = ActiveClients, @currCancel = CancelledClients
    FROM dbo.PlatformMetricsSnapshot ORDER BY Month DESC;
    SELECT @prevActive = ActiveClients, @prevCancel = CancelledClients FROM (
        SELECT ActiveClients, CancelledClients, ROW_NUMBER() OVER (ORDER BY Month DESC) AS rn
        FROM dbo.PlatformMetricsSnapshot
    ) x WHERE rn = 2;
    DECLARE @netGrowth int = ISNULL(@currActive,0) - ISNULL(@prevActive,0);
    DECLARE @newChurn  int = ISNULL(@currCancel,0) - ISNULL(@prevCancel,0);
    DECLARE @churnPct decimal(9,2) =
        CASE WHEN ISNULL(@prevActive,0) > 0 THEN CAST(@newChurn AS decimal(9,2)) / @prevActive * 100 ELSE 0 END;

    -- RS1: headline (active MRR live; net growth + churn from snapshots)
    SELECT
        ISNULL(SUM(CASE WHEN Status='active' THEN Mrr ELSE 0 END),0) AS TotalMrr,
        SUM(CASE WHEN Status='active' THEN 1 ELSE 0 END) AS ActiveCount,
        @netGrowth AS NetGrowth,
        @churnPct  AS GrossChurnPct
    FROM dbo.Tenants;

    -- RS2: per-plan performance
    SELECT PlanName, COUNT(*) AS Clients, ISNULL(SUM(Mrr),0) AS Mrr
    FROM dbo.Tenants WHERE PlanName IS NOT NULL GROUP BY PlanName ORDER BY SUM(Mrr) DESC;

    -- RS3: last 6 months revenue (paid invoices by PaidOn)
    ;WITH Months AS (
        SELECT DATEFROMPARTS(
            YEAR(DATEADD(MONTH, n, SYSUTCDATETIME())),
            MONTH(DATEADD(MONTH, n, SYSUTCDATETIME())), 1) AS M
        FROM (VALUES (-5),(-4),(-3),(-2),(-1),(0)) v(n)
    )
    SELECT
        FORMAT(m.M, 'MMM', 'en-US') AS Label,
        (SELECT ISNULL(SUM(Amount),0) FROM dbo.Invoices inv
         WHERE inv.Status = 'paid' AND inv.PaidOn >= m.M AND inv.PaidOn < DATEADD(MONTH, 1, m.M)) AS Revenue
    FROM Months m
    ORDER BY m.M;
END
