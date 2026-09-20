using FluentMigrator;

namespace Sms.Migrations;

[Migration(23, "Transport: Trips, TripPings (+TVP), Boardings with tenant RLS")]
public sealed class M0023_Transport_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Trips")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RouteId").AsGuid().Nullable()
            .WithColumn("BusNo").AsString(40).Nullable()
            .WithColumn("DriverId").AsGuid().Nullable()
            .WithColumn("ConductorId").AsGuid().Nullable()
            .WithColumn("Direction").AsString(10).NotNullable().WithDefaultValue("pickup")
            .WithColumn("Status").AsString(10).NotNullable().WithDefaultValue("live")
            .WithColumn("StartedAt").AsDateTime2().Nullable()
            .WithColumn("EndedAt").AsDateTime2().Nullable();
        Create.Index("IX_Trips_Driver_Status").OnTable("Trips")
            .OnColumn("DriverId").Ascending().OnColumn("Status").Ascending();

        Create.Table("TripPings")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TripId").AsGuid().NotNullable()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("SpeedKmh").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Heading").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("At").AsDateTime2().NotNullable();
        Create.Index("IX_TripPings_Trip_At").OnTable("TripPings")
            .OnColumn("TripId").Ascending().OnColumn("At").Ascending();

        Create.Table("Boardings")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TripId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("StopId").AsGuid().Nullable()
            .WithColumn("State").AsString(10).NotNullable().WithDefaultValue("pending")
            .WithColumn("At").AsDateTime2().NotNullable();
        Create.UniqueConstraint("UQ_Boardings_Trip_Student")
            .OnTable("Boardings").Columns("TenantId", "TripId", "StudentId");

        Execute.Sql(@"CREATE TYPE dbo.TripPingTvp AS TABLE
(Lat float, Lng float, SpeedKmh float, Heading float, At datetime2);");

        foreach (var t in new[] { "Trips", "TripPings", "Boardings" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "Boardings", "TripPings", "Trips" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.TripPingTvp;");
        Delete.Table("Boardings");
        Delete.Table("TripPings");
        Delete.Table("Trips");
    }
}
