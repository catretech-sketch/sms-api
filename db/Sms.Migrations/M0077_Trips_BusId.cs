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

        // Redeploy Trip_Start so new trips populate BusId (resolved per-tenant).
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport.Trip_Start"))
            Execute.Sql(sql);
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
