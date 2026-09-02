using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// Multi-tenant hardening: bind a Trip to a concrete <c>BusId</c> instead of matching
/// buses by the free-text <c>BusNo</c>. Bus numbers can repeat across schools, so live
/// tracking must key on TenantId + BusId (+ TripId), never on the number/route name.
/// RLS already prevents cross-tenant leakage; this closes the within-tenant / defence-in-depth
/// gap and satisfies the "identify buses by BusId" requirement.
/// </summary>
[Migration(77, "Trips.BusId: bind trips to a concrete tenant-scoped bus (replace BusNo matching)")]
public sealed class M0077_Trips_BusId : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Trips', 'BusId') IS NULL
    ALTER TABLE dbo.Trips ADD BusId uniqueidentifier NULL;
");

        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Trips_Tenant_Bus_Status' AND object_id = OBJECT_ID(N'dbo.Trips'))
    CREATE INDEX IX_Trips_Tenant_Bus_Status ON dbo.Trips (TenantId, BusId, Status);
");

        // Backfill existing trips from their BusNo, matched WITHIN THE SAME TENANT.
        // Elevate this connection to platform so the one-off backfill can see all rows;
        // the JOIN still binds each trip only to a bus of its own TenantId.
        Execute.Sql(@"
EXEC sp_set_session_context @key=N'IsPlatform', @value=1;
UPDATE t SET BusId = b.Id
FROM dbo.Trips t
JOIN dbo.Buses b ON b.TenantId = t.TenantId AND b.BusNo = t.BusNo
WHERE t.BusId IS NULL AND t.BusNo IS NOT NULL;
");

        // Redeploy Trip_Start so new trips populate BusId (resolved per-tenant). Frozen inline
        // (not sourced from the shared procs/transport/Trip_Start.sql resource) because that file
        // was later edited (M0163) to reference Buses.ConductorStaffId, a column that does not
        // exist yet at this point in migration history — a fresh-DB replay must see the proc as it
        // actually looked when this migration ran, not the file's current-tip content.
        Execute.Sql(@"
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
END");
    }

    public override void Down()
    {
        Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Trips_Tenant_Bus_Status' AND object_id = OBJECT_ID(N'dbo.Trips'))
    DROP INDEX IX_Trips_Tenant_Bus_Status ON dbo.Trips;
IF COL_LENGTH('dbo.Trips', 'BusId') IS NOT NULL
    ALTER TABLE dbo.Trips DROP COLUMN BusId;
");
    }
}
