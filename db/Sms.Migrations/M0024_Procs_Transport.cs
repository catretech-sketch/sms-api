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

        // Same reasoning as BusId above: Trip_Start.sql now also references
        // DriverLastPingAt/ConductorLastPingAt (added formally in M0163), so those columns
        // must exist before this guarded, currently-live-file-sourced CREATE PROCEDURE runs.
        Execute.Sql(@"
IF COL_LENGTH('dbo.Trips', 'DriverLastPingAt') IS NULL
    ALTER TABLE dbo.Trips ADD DriverLastPingAt datetime2 NULL;
IF COL_LENGTH('dbo.Trips', 'ConductorLastPingAt') IS NULL
    ALTER TABLE dbo.Trips ADD ConductorLastPingAt datetime2 NULL;
");

        // Same reasoning again: TripPing_BulkInsert.sql now references TripPings.Accuracy and
        // an Accuracy column on the dbo.TripPingTvp table type (both added formally in M0176).
        // dbo.TripPings and dbo.TripPingTvp already exist as of M0023, so — unlike a reference to
        // a table that doesn't exist yet, which SQL Server defers — a column missing from an
        // EXISTING object is caught immediately at CREATE PROCEDURE time. Guard both here so a
        // fresh migration run can compile the current-tip proc; this becomes a no-op once M0176
        // runs (which recreates the type unconditionally and guards the column add the same way).
        Execute.Sql(@"
IF COL_LENGTH('dbo.TripPings', 'Accuracy') IS NULL
    ALTER TABLE dbo.TripPings ADD Accuracy float NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id
    WHERE tt.name = 'TripPingTvp' AND c.name = 'Accuracy'
)
BEGIN
    DROP PROCEDURE IF EXISTS dbo.TripPing_BulkInsert;
    DROP TYPE IF EXISTS dbo.TripPingTvp;
    CREATE TYPE dbo.TripPingTvp AS TABLE
    (
        Lat float NOT NULL,
        Lng float NOT NULL,
        SpeedKmh float NOT NULL,
        Heading float NOT NULL,
        At datetime2 NOT NULL,
        Accuracy float NULL
    );
END
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
