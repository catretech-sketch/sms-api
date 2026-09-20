using FluentMigrator;

namespace Sms.Migrations;

[Migration(176, "TripStopProgress table, Trips.CurrentStopId, TripPings.Accuracy")]
public sealed class M0176_TripStopProgress_And_PingAccuracy : Migration
{
    public override void Up()
    {
        Create.Table("TripStopProgress")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TripId").AsGuid().NotNullable()
            .WithColumn("StopId").AsGuid().NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("ArrivedAt").AsDateTime2().Nullable()
            .WithColumn("ConfirmedAt").AsDateTime2().Nullable()
            .WithColumn("DepartedAt").AsDateTime2().Nullable();
        Create.Index("IX_TripStopProgress_Trip_Seq").OnTable("TripStopProgress")
            .OnColumn("TripId").Ascending().OnColumn("Seq").Ascending();
        Create.Index("IX_TripStopProgress_Trip_Stop").OnTable("TripStopProgress")
            .OnColumn("TripId").Ascending().OnColumn("StopId").Ascending().WithOptions().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.TripStopProgressTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TripStopProgress,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TripStopProgress AFTER INSERT
WITH (STATE = ON);");

        Alter.Table("Trips").AddColumn("CurrentStopId").AsGuid().Nullable();

        // Use a guarded raw ALTER rather than Alter.Table().AddColumn(): M0024's guarded reload
        // of procs.transport. (TripPing_BulkInsert.sql references this column) may already have
        // added it on a fresh migration run, and Alter.Table().AddColumn() is not idempotent.
        Execute.Sql(@"
IF COL_LENGTH('dbo.TripPings', 'Accuracy') IS NULL
    ALTER TABLE dbo.TripPings ADD Accuracy float NULL;
");

        // SQL Server has no ALTER TYPE for table types — drop and recreate, and the
        // consuming proc must be dropped first since it references the type signature.
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripPing_BulkInsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.TripPingTvp;");
        Execute.Sql(@"CREATE TYPE dbo.TripPingTvp AS TABLE
(
    Lat float NOT NULL,
    Lng float NOT NULL,
    SpeedKmh float NOT NULL,
    Heading float NOT NULL,
    At datetime2 NOT NULL,
    Accuracy float NULL
);");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.tripstops."))
            Execute.Sql(sql);
        // TripPing_BulkInsert.sql lives under procs.transport. and is re-loaded here
        // (its own migration's EmbeddedProcs call already ran once; this CREATE OR ALTER
        // re-applies the updated version now that the TVP shape changed). Verified every
        // .sql file under procs/transport/ uses CREATE OR ALTER PROCEDURE, so this whole-
        // folder reload is safe to re-run.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripStopProgress_Complete;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripStopProgress_ConfirmArrival;");

        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TripPing_BulkInsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.TripPingTvp;");
        Execute.Sql(@"CREATE TYPE dbo.TripPingTvp AS TABLE
(
    Lat float NOT NULL,
    Lng float NOT NULL,
    SpeedKmh float NOT NULL,
    Heading float NOT NULL,
    At datetime2 NOT NULL
);");
        // Frozen inline (not sourced from the shared procs/transport/TripPing_BulkInsert.sql
        // resource) because that file was edited by this same migration's Up() to reference
        // Accuracy — embedded resources always reflect current on-disk content, so reloading
        // the folder here would recreate the proc against the just-recreated 5-column TVP
        // above while the file itself still selects the 6th (Accuracy) column, throwing
        // "Invalid column name 'Accuracy'". This is exactly the M0077_Trips_BusId.cs pattern:
        // a rollback must see the proc as it looked BEFORE this migration ran, not the file's
        // current-tip content.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.TripPing_BulkInsert
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @Rows dbo.TripPingTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At)
    SELECT NEWID(), @TenantId, @TripId, Lat, Lng, SpeedKmh, Heading, At FROM @Rows;
END");

        Delete.Column("Accuracy").FromTable("TripPings");
        Delete.Column("CurrentStopId").FromTable("Trips");

        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TripStopProgressTenantPolicy;");
        Delete.Table("TripStopProgress");

        // Note: the guard patch added to M0024_Procs_Transport.cs's Up() (TripPings.Accuracy /
        // TripPingTvp recreation before its procs.transport. reload) is intentionally NOT
        // reverted here. It is a permanent, harmless no-op once this migration has run either
        // direction — same as M0024's pre-existing BusId/DriverLastPingAt guards, which are
        // never undone by any Down() either.
    }
}
