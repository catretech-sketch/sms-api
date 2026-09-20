using FluentMigrator;

namespace Sms.Migrations;

[Migration(165, "Leave proc: Leave_Balances")]
public sealed class M0165_Procs_LeaveBalances : Migration
{
    public override void Up()
    {
        // Exact fragment, not the broad "procs.leave." prefix: that prefix also matches
        // procs/leave/Leave_Create.sql and procs/leave/Leave_Decide.sql — stale M0030-era
        // proc bodies that would otherwise re-run here at version 165 and clobber the
        // newer versions M0093/M0113/M0146 already established. Same pitfall as M0093's
        // own comment about M0030's broad prefix, one level further down the timeline.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leave.Leave_Balances"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Leave_Balances;");
    }
}
