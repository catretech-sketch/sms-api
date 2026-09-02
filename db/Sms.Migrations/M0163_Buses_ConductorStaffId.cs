using FluentMigrator;

namespace Sms.Migrations;

[Migration(163, "Transport: Buses.ConductorStaffId + conductor-aware Bus_Update/Bus_Create + Trip_Start auto-assigns ConductorId")]
public sealed class M0163_Buses_ConductorStaffId : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'ConductorStaffId') IS NULL
    ALTER TABLE dbo.Buses ADD ConductorStaffId uniqueidentifier NULL;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0,
    @ConductorStaffId uniqueidentifier = NULL,
    @ClearConductor bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;
    IF @ClearConductor = 1 SET @ConductorStaffId = NULL;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId AND Id <> @BusId;

        UPDATE b SET
            b.DriverStaffId = @DriverStaffId,
            b.Driver = s.Name,
            b.DriverPhone = s.Phone
        FROM dbo.Buses b
        INNER JOIN dbo.Staff s ON s.Id = @DriverStaffId AND s.TenantId = @TenantId
        WHERE b.Id = @BusId;
    END
    ELSE IF @ClearDriver = 1
        UPDATE dbo.Buses SET DriverStaffId = NULL, Driver = NULL, DriverPhone = NULL WHERE Id = @BusId;

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId AND Id <> @BusId;

        UPDATE dbo.Buses SET ConductorStaffId = @ConductorStaffId WHERE Id = @BusId AND TenantId = @TenantId;
    END
    ELSE IF @ClearConductor = 1
        UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @BusId;

    UPDATE b SET
        b.BusNo = COALESCE(@BusNo, b.BusNo),
        b.RouteId = CASE WHEN @RouteId IS NOT NULL THEN @RouteId ELSE b.RouteId END,
        b.RouteName = CASE WHEN @RouteId IS NOT NULL THEN r.Name ELSE b.RouteName END
    FROM dbo.Buses b
    LEFT JOIN dbo.TransportRoutes r ON r.Id = @RouteId AND r.TenantId = @TenantId
    WHERE b.Id = @BusId AND b.TenantId = @TenantId;

    -- Column order here must match UpdatedBusRow's constructor parameter order exactly:
    -- Dapper's fast-path record materializer requires an exact positional match when the
    -- column count equals the constructor's parameter count, so ConductorStaffId (added last
    -- to the C# record to stay source-compatible) must also be selected last here.
    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        CASE WHEN b.RouteId IS NOT NULL
            THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId)
            ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END AS StopCount,
        (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned,
        b.ConductorStaffId
    FROM dbo.Buses b WHERE b.Id = @BusId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ConductorStaffId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @ResolvedRouteId uniqueidentifier = @RouteId;

    IF @ResolvedRouteId IS NULL AND @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @ResolvedRouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId;

        SELECT @Driver = s.Name, @DriverPhone = s.Phone
        FROM dbo.Staff s WHERE s.Id = @DriverStaffId AND s.TenantId = @TenantId;
    END

    IF @ConductorStaffId IS NOT NULL
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId;

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId, ConductorStaffId)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId, @ConductorStaffId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        b.ConductorStaffId,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport.Trip_Start"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'ConductorStaffId') IS NOT NULL
    ALTER TABLE dbo.Buses DROP COLUMN ConductorStaffId;");
    }
}
