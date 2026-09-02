using FluentMigrator;

namespace Sms.Migrations;

[Migration(165, "Leave proc: Leave_Balances")]
public sealed class M0165_Procs_LeaveBalances : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leave."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Leave_Balances;");
    }
}
