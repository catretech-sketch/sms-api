CREATE OR ALTER PROCEDURE dbo.Trip_Start
    @TenantId uniqueidentifier, @RouteId uniqueidentifier, @BusNo nvarchar(40),
    @DriverId uniqueidentifier, @Direction nvarchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();

    -- Bind the trip to a concrete BusId resolved WITHIN THIS TENANT. Never trust the
    -- bus number alone: a bus number can repeat across schools, so we scope the lookup
    -- by @TenantId (belt-and-suspenders on top of RLS) so a trip can only ever point at
    -- this tenant's bus. Live tracking then matches on BusId, not the number.
    DECLARE @BusId uniqueidentifier =
        (SELECT TOP 1 Id FROM dbo.Buses
         WHERE TenantId = @TenantId AND BusNo = @BusNo ORDER BY Id);

    INSERT dbo.Trips (Id, TenantId, RouteId, BusId, BusNo, DriverId, Direction, Status, StartedAt)
    VALUES (@Id, @TenantId, @RouteId, @BusId, @BusNo, @DriverId, ISNULL(@Direction, 'pickup'), 'live', SYSUTCDATETIME());

    SELECT Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt
    FROM dbo.Trips WHERE Id = @Id;
END
