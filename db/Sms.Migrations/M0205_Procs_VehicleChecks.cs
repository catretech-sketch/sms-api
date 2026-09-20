using FluentMigrator;

namespace Sms.Migrations;

[Migration(205, "VehicleChecks procs: VehicleInspection_Upsert, FuelLog_Create")]
public sealed class M0205_Procs_VehicleChecks : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.vehiclechecks."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "VehicleInspection_Upsert", "FuelLog_Create" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
