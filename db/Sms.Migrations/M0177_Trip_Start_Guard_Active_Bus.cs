using FluentMigrator;

namespace Sms.Migrations;

[Migration(177, "Trip_Start: reject starting a second trip on a bus that already has one live")]
public sealed class M0177_Trip_Start_Guard_Active_Bus : Migration
{
    public override void Up()
    {
        // Trip_Start.sql now returns no row (instead of inserting) when the resolved BusId
        // already has a live trip. M0024's Up() already reloads procs.transport. on a fresh
        // migration run (embedded resources reflect current-tip file content), but M0024 has
        // already run on any previously-migrated database, so this new migration re-applies
        // the updated Trip_Start.sql there too — same pattern as M0176's re-application of
        // TripPing_BulkInsert.sql after M0024 had already created the original version.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No schema/object to roll back beyond the proc body itself, and CREATE OR ALTER is
        // not naturally reversible to "the previous version" — mirrors M0176's Down(), which
        // also leaves proc-reload guards in place permanently once applied.
    }
}
