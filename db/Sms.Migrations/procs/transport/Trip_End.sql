CREATE OR ALTER PROCEDURE dbo.Trip_End
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Trips SET Status = 'ended', EndedAt = SYSUTCDATETIME() WHERE Id = @Id AND Status = 'live';

    SELECT Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt,
        DriverLastPingAt, ConductorLastPingAt
    FROM dbo.Trips WHERE Id = @Id;
END
