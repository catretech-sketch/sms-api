CREATE OR ALTER PROCEDURE dbo.PlatformMetrics_UpsertCurrentMonth
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @month date = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
    DECLARE @mrr       decimal(18,2) = (SELECT ISNULL(SUM(CASE WHEN Status='active' THEN Mrr ELSE 0 END),0) FROM dbo.Tenants);
    DECLARE @active    int           = (SELECT COUNT(*) FROM dbo.Tenants WHERE Status='active');
    DECLARE @cancelled int           = (SELECT COUNT(*) FROM dbo.Tenants WHERE Status='cancelled');

    MERGE dbo.PlatformMetricsSnapshot AS t
    USING (SELECT @month AS Month) AS s ON t.Month = s.Month
    WHEN MATCHED THEN
        UPDATE SET Mrr = @mrr, ActiveClients = @active, CancelledClients = @cancelled
    WHEN NOT MATCHED THEN
        INSERT (Month, Mrr, ActiveClients, CancelledClients, CreatedAt)
        VALUES (@month, @mrr, @active, @cancelled, SYSUTCDATETIME());
END
