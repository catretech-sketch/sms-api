using FluentMigrator;

namespace Sms.Migrations;

[Migration(24, "Transport procs: Trip_Start/End, TripPing_BulkInsert (TVP), Boarding_Upsert")]
public sealed class M0024_Procs_Transport : Migration
{
    public override void Up()
    {
        // Trip_Start references Trips.BusId from its first creation here. SQL Server validates
        // columns of EXISTING tables at CREATE PROCEDURE time (deferred name resolution only
        // covers missing tables, not missing columns), so the column must exist before the proc
        // is created. It is formally added + backfilled later in M0077; this guarded add keeps the
        // proc valid on a fresh migration run and is a no-op once M0077 runs.
        Execute.Sql(@"
IF COL_LENGTH('dbo.Trips', 'BusId') IS NULL
    ALTER TABLE dbo.Trips ADD BusId uniqueidentifier NULL;
");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Trip_Start", "Trip_End", "TripPing_BulkInsert", "Boarding_Upsert" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
