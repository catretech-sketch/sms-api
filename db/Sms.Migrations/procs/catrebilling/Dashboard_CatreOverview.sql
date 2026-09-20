CREATE OR ALTER PROCEDURE dbo.Dashboard_CatreOverview
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: headline counts + MRR
    SELECT
        COUNT(*) AS Total,
        SUM(CASE WHEN Status = 'active'    THEN 1 ELSE 0 END) AS Active,
        SUM(CASE WHEN Status = 'trial'     THEN 1 ELSE 0 END) AS Trial,
        SUM(CASE WHEN Status = 'suspended' THEN 1 ELSE 0 END) AS Suspended,
        SUM(CASE WHEN Status = 'cancelled' THEN 1 ELSE 0 END) AS Cancelled,
        ISNULL(SUM(CASE WHEN Status = 'active' THEN Mrr ELSE 0 END), 0) AS Mrr,
        SUM(CASE WHEN Status = 'trial' THEN 1 ELSE 0 END) AS TrialsEnding
    FROM dbo.Tenants;

    -- Result set 2: plan mix (by tier)
    SELECT Tier AS Label, COUNT(*) AS Value
    FROM dbo.Tenants
    WHERE Tier IS NOT NULL
    GROUP BY Tier;
END
