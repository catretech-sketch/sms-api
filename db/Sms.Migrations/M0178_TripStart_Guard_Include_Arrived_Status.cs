using FluentMigrator;

namespace Sms.Migrations;

[Migration(178, "Trip_Start: widen the active-bus guard to also cover the future 'arrived' trip status")]
public sealed class M0178_TripStart_Guard_Include_Arrived_Status : Migration
{
    public override void Up()
    {
        // Trip_Start.sql's duplicate-active-trip guard now checks
        // Status IN ('live', 'arrived') instead of Status = 'live' only. No Trips row can be
        // 'arrived' yet as of this migration, so this is behaviorally identical to the guard
        // M0177 shipped — it is forward-compatible so a later 'arrived' status (a still-active
        // pickup trip that has reached school, before a possible return leg) does not silently
        // bypass the guard the day that status starts being written. Same reload mechanics as
        // M0177/M0176: M0024's Up() only re-picks-up current file content on a from-scratch
        // migration run, so a database that already ran M0024 (and M0177) needs this migration
        // to redeploy the updated proc body.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No schema/object to roll back beyond the proc body itself, and CREATE OR ALTER is
        // not naturally reversible to "the previous version" — mirrors M0177's/M0176's Down(),
        // which also leave proc-reload guards in place permanently once applied.
    }
}
