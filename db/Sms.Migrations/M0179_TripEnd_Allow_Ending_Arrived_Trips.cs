using FluentMigrator;

namespace Sms.Migrations;

[Migration(179, "Trip_End: allow ending an 'arrived' trip; Trips: persist school-arrival timestamp/location")]
public sealed class M0179_TripEnd_Allow_Ending_Arrived_Trips : Migration
{
    public override void Up()
    {
        // dbo.Trip_End previously matched WHERE Status = 'live' only, so a trip that had
        // already transitioned to 'arrived' (via school-arrived) silently failed to end:
        // the UPDATE matched zero rows but the proc's trailing SELECT still returned the
        // row and the endpoint still returned 200, leaving Status stuck at 'arrived' and
        // EndedAt NULL forever. Combined with Trip_Start's guard (M0177/M0178) blocking a
        // new trip on any bus with Status IN ('live','arrived'), this permanently blocked
        // that bus from ever starting a return/drop leg. Trip_End.sql now matches
        // Status IN ('live', 'arrived').
        Alter.Table("Trips")
            .AddColumn("SchoolArrivedAt").AsDateTime2().Nullable()
            .AddColumn("SchoolArrivedLat").AsDouble().Nullable()
            .AddColumn("SchoolArrivedLng").AsDouble().Nullable();

        // Same reload mechanics as M0177/M0178: M0024's Up() only re-picks-up current file
        // content on a from-scratch migration run, so a database that already ran M0024 (and
        // M0177/M0178) needs this migration to redeploy the updated Trip_End proc body.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // The proc body is not reversible to "the previous version" (CREATE OR ALTER isn't
        // naturally undoable) — mirrors M0177's/M0178's Down(), which also leave proc-reload
        // guards in place permanently once applied. The added columns, however, are a normal
        // reversible schema change, so those are rolled back here.
        Delete.Column("SchoolArrivedLng").FromTable("Trips");
        Delete.Column("SchoolArrivedLat").FromTable("Trips");
        Delete.Column("SchoolArrivedAt").FromTable("Trips");
    }
}
