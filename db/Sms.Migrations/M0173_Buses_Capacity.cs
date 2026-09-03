using FluentMigrator;

namespace Sms.Migrations;

[Migration(173, "Transport: Buses.Capacity for occupancy enforcement")]
public sealed class M0173_Buses_Capacity : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'Capacity') IS NULL
    ALTER TABLE dbo.Buses ADD Capacity int NULL;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ConductorStaffId uniqueidentifier = NULL,
    @Capacity int = NULL,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @ResolvedRouteId uniqueidentifier = @RouteId;

    IF @ResolvedRouteId IS NULL AND @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @ResolvedRouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'driver' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId);
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId;

        SELECT @Driver = s.Name, @DriverPhone = s.Phone
        FROM dbo.Staff s WHERE s.Id = @DriverStaffId AND s.TenantId = @TenantId;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'conductor' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId);
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId;
    END

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId, ConductorStaffId, Capacity)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId, @ConductorStaffId, @Capacity);

    IF @DriverStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
    IF @ConductorStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.Driver, b.DriverPhone,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status, b.ConductorStaffId, b.Capacity
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0,
    @ConductorStaffId uniqueidentifier = NULL,
    @ClearConductor bit = 0,
    @Capacity int = NULL,
    @ClearCapacity bit = 0,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    DECLARE @OldDriverStaffId uniqueidentifier, @OldConductorStaffId uniqueidentifier;
    SELECT @OldDriverStaffId = DriverStaffId, @OldConductorStaffId = ConductorStaffId
    FROM dbo.Buses WHERE Id = @BusId;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;
    IF @ClearConductor = 1 SET @ConductorStaffId = NULL;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId AND Id <> @BusId);
        IF @StolenFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenFromBusId AND StaffId = @DriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET DriverStaffId = NULL WHERE Id = @StolenFromBusId;
        END

        UPDATE b SET
            b.DriverStaffId = @DriverStaffId,
            b.Driver = s.Name,
            b.DriverPhone = s.Phone
        FROM dbo.Buses b
        INNER JOIN dbo.Staff s ON s.Id = @DriverStaffId AND s.TenantId = @TenantId
        WHERE b.Id = @BusId;

        IF @OldDriverStaffId IS NULL OR @OldDriverStaffId <> @DriverStaffId
        BEGIN
            IF @OldDriverStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                    AND Role = 'driver' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearDriver = 1
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL, Driver = NULL, DriverPhone = NULL WHERE Id = @BusId;
        IF @OldDriverStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenConductorFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId AND Id <> @BusId);
        IF @StolenConductorFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenConductorFromBusId AND StaffId = @ConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @StolenConductorFromBusId;
        END

        UPDATE dbo.Buses SET ConductorStaffId = @ConductorStaffId WHERE Id = @BusId AND TenantId = @TenantId;

        IF @OldConductorStaffId IS NULL OR @OldConductorStaffId <> @ConductorStaffId
        BEGIN
            IF @OldConductorStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                    AND Role = 'conductor' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearConductor = 1
    BEGIN
        UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @BusId;
        IF @OldConductorStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
    END

    UPDATE b SET
        b.BusNo = COALESCE(@BusNo, b.BusNo),
        b.RouteId = CASE WHEN @RouteId IS NOT NULL THEN @RouteId ELSE b.RouteId END,
        b.RouteName = CASE WHEN @RouteId IS NOT NULL THEN r.Name ELSE b.RouteName END,
        b.Capacity = CASE WHEN @ClearCapacity = 1 THEN NULL WHEN @Capacity IS NOT NULL THEN @Capacity ELSE b.Capacity END
    FROM dbo.Buses b
    LEFT JOIN dbo.TransportRoutes r ON r.Id = @RouteId AND r.TenantId = @TenantId
    WHERE b.Id = @BusId AND b.TenantId = @TenantId;

    -- Column order here must match UpdatedBusRow's constructor parameter order exactly (see M0169):
    -- Dapper's fast-path record materializer requires an exact positional match when the column
    -- count equals the constructor's parameter count. Capacity is appended last to match the
    -- newly-added trailing constructor parameter (Task 2).
    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        CASE WHEN b.RouteId IS NOT NULL
            THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId)
            ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END AS StopCount,
        (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned,
        b.ConductorStaffId, b.Capacity
    FROM dbo.Buses b WHERE b.Id = @BusId;
END");
    }

    public override void Down()
    {
        // Revert Bus_Create / Bus_Update back to their pre-M0173 (post-M0169) bodies —
        // no @Capacity/@ClearCapacity params, no Capacity column in INSERT/UPDATE/SELECT —
        // before dropping the column, so the procs never reference a dropped column.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ConductorStaffId uniqueidentifier = NULL,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @ResolvedRouteId uniqueidentifier = @RouteId;

    IF @ResolvedRouteId IS NULL AND @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @ResolvedRouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'driver' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId);
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId;

        SELECT @Driver = s.Name, @DriverPhone = s.Phone
        FROM dbo.Staff s WHERE s.Id = @DriverStaffId AND s.TenantId = @TenantId;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'conductor' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId);
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId;
    END

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId, ConductorStaffId)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId, @ConductorStaffId);

    IF @DriverStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
    IF @ConductorStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.Driver, b.DriverPhone,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status, b.ConductorStaffId
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0,
    @ConductorStaffId uniqueidentifier = NULL,
    @ClearConductor bit = 0,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    DECLARE @OldDriverStaffId uniqueidentifier, @OldConductorStaffId uniqueidentifier;
    SELECT @OldDriverStaffId = DriverStaffId, @OldConductorStaffId = ConductorStaffId
    FROM dbo.Buses WHERE Id = @BusId;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;
    IF @ClearConductor = 1 SET @ConductorStaffId = NULL;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId AND Id <> @BusId);
        IF @StolenFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenFromBusId AND StaffId = @DriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET DriverStaffId = NULL WHERE Id = @StolenFromBusId;
        END

        UPDATE b SET
            b.DriverStaffId = @DriverStaffId,
            b.Driver = s.Name,
            b.DriverPhone = s.Phone
        FROM dbo.Buses b
        INNER JOIN dbo.Staff s ON s.Id = @DriverStaffId AND s.TenantId = @TenantId
        WHERE b.Id = @BusId;

        IF @OldDriverStaffId IS NULL OR @OldDriverStaffId <> @DriverStaffId
        BEGIN
            IF @OldDriverStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                    AND Role = 'driver' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearDriver = 1
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL, Driver = NULL, DriverPhone = NULL WHERE Id = @BusId;
        IF @OldDriverStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenConductorFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId AND Id <> @BusId);
        IF @StolenConductorFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenConductorFromBusId AND StaffId = @ConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @StolenConductorFromBusId;
        END

        UPDATE dbo.Buses SET ConductorStaffId = @ConductorStaffId WHERE Id = @BusId AND TenantId = @TenantId;

        IF @OldConductorStaffId IS NULL OR @OldConductorStaffId <> @ConductorStaffId
        BEGIN
            IF @OldConductorStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                    AND Role = 'conductor' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearConductor = 1
    BEGIN
        UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @BusId;
        IF @OldConductorStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
    END

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

        Execute.Sql("IF COL_LENGTH('dbo.Buses', 'Capacity') IS NOT NULL ALTER TABLE dbo.Buses DROP COLUMN Capacity;");
    }
}
