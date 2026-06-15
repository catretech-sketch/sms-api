CREATE OR ALTER PROCEDURE dbo.Trip_Start
    @TenantId uniqueidentifier, @RouteId uniqueidentifier, @BusNo nvarchar(40),
    @DriverId uniqueidentifier, @Direction nvarchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Trips (Id, TenantId, RouteId, BusNo, DriverId, Direction, Status, StartedAt)
    VALUES (@Id, @TenantId, @RouteId, @BusNo, @DriverId, ISNULL(@Direction, 'pickup'), 'live', SYSUTCDATETIME());

    SELECT Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt
    FROM dbo.Trips WHERE Id = @Id;
END
