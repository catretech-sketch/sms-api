using FluentMigrator;

namespace Sms.Migrations;

[Migration(14, "Staffing procs: Teacher_Create/Update + Staff_Create/Update (embedded CREATE OR ALTER)")]
public sealed class M0014_Procs_Staffing : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Teacher_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Teacher_Update;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_Update;");
    }
}
