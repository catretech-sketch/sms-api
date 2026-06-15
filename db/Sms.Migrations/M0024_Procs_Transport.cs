using FluentMigrator;

namespace Sms.Migrations;

[Migration(24, "Transport procs: Trip_Start/End, TripPing_BulkInsert (TVP), Boarding_Upsert")]
public sealed class M0024_Procs_Transport : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Trip_Start", "Trip_End", "TripPing_BulkInsert", "Boarding_Upsert" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
