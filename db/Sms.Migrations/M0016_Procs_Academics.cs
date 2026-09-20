using FluentMigrator;

namespace Sms.Migrations;

[Migration(16, "Academics procs: Class_Create, Subject_Create, Attendance_BulkUpsert (TVP)")]
public sealed class M0016_Procs_Academics : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.academics."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Class_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Subject_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Attendance_BulkUpsert;");
    }
}
