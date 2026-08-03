using FluentMigrator;

namespace Sms.Migrations;

[Migration(109, "Geofence proc: SchoolLocation_Delete")]
public sealed class M0109_SchoolLocation_Delete : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.checkin.SchoolLocation_Delete"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.SchoolLocation_Delete;");
    }
}
