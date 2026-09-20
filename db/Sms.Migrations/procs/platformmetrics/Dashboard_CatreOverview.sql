CREATE OR ALTER PROCEDURE dbo.Dashboard_CatreOverview
AS
BEGIN
    SET NOCOUNT ON;

    -- Churn (month-over-month) from the two latest snapshots: newly-cancelled / prior active.
    DECLARE @currCancel int, @prevActive int, @prevCancel int;
    SELECT TOP 1 @currCancel = CancelledClients FROM dbo.PlatformMetricsSnapshot ORDER BY Month DESC;
    SELECT @prevActive = ActiveClients, @prevCancel = CancelledClients FROM (
        SELECT ActiveClients, CancelledClients, ROW_NUMBER() OVER (ORDER BY Month DESC) AS rn
        FROM dbo.PlatformMetricsSnapshot
    ) x WHERE rn = 2;
    DECLARE @newChurn int = ISNULL(@currCancel,0) - ISNULL(@prevCancel,0);
    DECLARE @churnPct decimal(9,2) =
        CASE WHEN ISNULL(@prevActive,0) > 0 THEN CAST(@newChurn AS decimal(9,2)) / @prevActive * 100 ELSE 0 END;

    -- RS1: headline counts + MRR + churn
    SELECT
        COUNT(*) AS Total,
        SUM(CASE WHEN Status = 'active'    THEN 1 ELSE 0 END) AS Active,
        SUM(CASE WHEN Status = 'trial'     THEN 1 ELSE 0 END) AS Trial,
        SUM(CASE WHEN Status = 'suspended' THEN 1 ELSE 0 END) AS Suspended,
        SUM(CASE WHEN Status = 'cancelled' THEN 1 ELSE 0 END) AS Cancelled,
        ISNULL(SUM(CASE WHEN Status = 'active' THEN Mrr ELSE 0 END), 0) AS Mrr,
        SUM(CASE WHEN Status = 'trial' THEN 1 ELSE 0 END) AS TrialsEnding,
        @churnPct AS ChurnPct
    FROM dbo.Tenants;

    -- RS2: plan mix by tier
    SELECT Tier AS Label, COUNT(*) AS Value
    FROM dbo.Tenants WHERE Tier IS NOT NULL GROUP BY Tier;

    -- RS3: recent activity (latest 20 audit entries)
    SELECT TOP 20 ActorName AS Actor, Action, Target, Kind, At
    FROM dbo.AuditLog ORDER BY At DESC;

    -- RS4: usage alerts (>= 80% of a plan limit)
    SELECT Name AS Tenant, 'students' AS Metric, StudentsCount AS Used, LimitsStudents AS [Limit],
           CAST(StudentsCount * 100 / NULLIF(LimitsStudents, 0) AS int) AS Pct
    FROM dbo.Tenants
    WHERE LimitsStudents > 0 AND StudentsCount * 100 >= LimitsStudents * 80
    UNION ALL
    SELECT Name, 'storage', CAST(StorageGb AS int), LimitsStorageGb,
           CAST(StorageGb * 100 / NULLIF(LimitsStorageGb, 0) AS int)
    FROM dbo.Tenants
    WHERE LimitsStorageGb > 0 AND StorageGb * 100 >= LimitsStorageGb * 80;

    -- RS5: last 6 months — MRR (snapshot) + signups (live from Subscriptions)
    ;WITH Months AS (
        SELECT DATEFROMPARTS(
            YEAR(DATEADD(MONTH, n, SYSUTCDATETIME())),
            MONTH(DATEADD(MONTH, n, SYSUTCDATETIME())), 1) AS M
        FROM (VALUES (-5),(-4),(-3),(-2),(-1),(0)) v(n)
    )
    SELECT
        FORMAT(m.M, 'MMM', 'en-US') AS Label,
        ISNULL(s.Mrr, 0) AS Mrr,
        (SELECT COUNT(*) FROM dbo.Subscriptions sub
         WHERE sub.StartedAt >= m.M AND sub.StartedAt < DATEADD(MONTH, 1, m.M)) AS Signups
    FROM Months m
    LEFT JOIN dbo.PlatformMetricsSnapshot s ON s.Month = m.M
    ORDER BY m.M;
END
