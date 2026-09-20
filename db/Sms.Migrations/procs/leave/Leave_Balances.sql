CREATE OR ALTER PROCEDURE dbo.Leave_Balances
    @TenantId uniqueidentifier, @RequesterId uniqueidentifier, @Year int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.Type,
        e.TotalDays AS Total,
        ISNULL(SUM(DATEDIFF(day, r.FromDate, r.ToDate) + 1), 0) AS Used
    FROM dbo.LeaveEntitlements e
    LEFT JOIN dbo.LeaveRequests r
        ON r.TenantId = e.TenantId AND r.RequesterId = e.RequesterId AND r.Type = e.Type
        AND r.Status = 'approved' AND YEAR(r.FromDate) = @Year
    WHERE e.TenantId = @TenantId AND e.RequesterId = @RequesterId AND e.Year = @Year
    GROUP BY e.Type, e.TotalDays
    ORDER BY e.Type;
END
