using FluentMigrator;

namespace Sms.Migrations;

[Migration(30, "Workflow procs: Leave_Create + Leave_Decide")]
public sealed class M0030_Procs_Leave : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leave."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Leave_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Leave_Decide;");
    }
}
