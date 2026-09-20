using FluentMigrator;

namespace Sms.Migrations;

[Migration(114, "Buses: DriverStaffId FK + Bus_Update proc")]
public sealed class M0114_Buses_DriverStaffId : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'DriverStaffId') IS NULL
    ALTER TABLE dbo.Buses ADD DriverStaffId uniqueidentifier NULL;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;

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

    UPDATE b SET
        b.BusNo = COALESCE(@BusNo, b.BusNo),
        b.RouteId = CASE WHEN @RouteId IS NOT NULL THEN @RouteId ELSE b.RouteId END,
        b.RouteName = CASE WHEN @RouteId IS NOT NULL THEN r.Name ELSE b.RouteName END
    FROM dbo.Buses b
    LEFT JOIN dbo.TransportRoutes r ON r.Id = @RouteId AND r.TenantId = @TenantId
    WHERE b.Id = @BusId AND b.TenantId = @TenantId;

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        CASE WHEN b.RouteId IS NOT NULL
            THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId)
            ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END AS StopCount,
        (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned
    FROM dbo.Buses b WHERE b.Id = @BusId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL
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

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status
    FROM dbo.Buses b WHERE b.Id = @Id;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Bus_Update;");
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'DriverStaffId') IS NOT NULL
    ALTER TABLE dbo.Buses DROP COLUMN DriverStaffId;");
    }
}
