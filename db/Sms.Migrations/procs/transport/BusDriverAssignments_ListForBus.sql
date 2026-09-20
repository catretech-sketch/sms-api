CREATE OR ALTER PROCEDURE dbo.BusDriverAssignments_ListForBus
    @TenantId uniqueidentifier, @BusId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.Id, a.StaffId, s.Name AS StaffName, a.Role, a.AssignedAt, a.UnassignedAt
    FROM dbo.BusDriverAssignments a
    INNER JOIN dbo.Staff s ON s.Id = a.StaffId
    WHERE a.TenantId = @TenantId AND a.BusId = @BusId
    ORDER BY a.AssignedAt DESC;
END
