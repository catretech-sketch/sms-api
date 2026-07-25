using FluentMigrator;

namespace Sms.Migrations;

[Migration(93, "Leave_Create/Leave_Decide: accept/return Priority (embedded CREATE OR ALTER)")]
public sealed class M0093_Leave_Create_Priority : Migration
{
    public override void Up()
    {
        // Kept under procs/leaveidentity (not procs/leave) so M0030's broad "procs.leave."
        // EmbeddedProcs fragment doesn't pick these bodies up and re-create them
        // referencing Priority ~60 migrations before M0092 adds the column - the same
        // ordering pitfall fixed for M0086/M0087/M0089/M0091.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leaveidentity.Leave_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leaveidentity.Leave_Decide"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: previous proc bodies are superseded, not restored.
    }
}
