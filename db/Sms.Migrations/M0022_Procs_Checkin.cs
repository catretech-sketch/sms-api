using FluentMigrator;

namespace Sms.Migrations;

[Migration(22, "Geofence procs: SchoolLocation_Upsert + CheckIn_Insert (embedded CREATE OR ALTER)")]
public sealed class M0022_Procs_Checkin : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.checkin."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.SchoolLocation_Upsert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.CheckIn_Insert;");
    }
}
