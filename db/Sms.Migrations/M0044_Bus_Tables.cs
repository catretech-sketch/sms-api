using FluentMigrator;

namespace Sms.Migrations;

[Migration(44, "Bus duty: Buses + BusStops + BusAssignments master tables with tenant RLS")]
public sealed class M0044_Bus_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Buses")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusNo").AsString(40).NotNullable()
            .WithColumn("RouteName").AsString(80).Nullable()
            .WithColumn("Driver").AsString(120).Nullable()
            .WithColumn("DriverPhone").AsString(32).Nullable();
        Create.Index("IX_Buses_Tenant").OnTable("Buses").OnColumn("TenantId").Ascending();

        Create.Table("BusStops")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Time").AsString(10).Nullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0);
        Create.Index("IX_BusStops_Bus").OnTable("BusStops").OnColumn("BusId").Ascending();

        Create.Table("BusAssignments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TeacherUserId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable();
        Execute.Sql(
            "CREATE UNIQUE INDEX IX_BusAssignments_Teacher ON dbo.BusAssignments (TenantId, TeacherUserId);");

        foreach (var t in new[] { "Buses", "BusStops", "BusAssignments" })
            Execute.Sql($@"CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "Buses", "BusStops", "BusAssignments" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("BusAssignments");
        Delete.Table("BusStops");
        Delete.Table("Buses");
    }
}
