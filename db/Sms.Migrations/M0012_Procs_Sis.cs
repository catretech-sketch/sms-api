using FluentMigrator;

namespace Sms.Migrations;

[Migration(12, "SIS procs: Student_Create + Student_Update (embedded CREATE OR ALTER)")]
public sealed class M0012_Procs_Sis : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Student_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Student_Update;");
    }
}
