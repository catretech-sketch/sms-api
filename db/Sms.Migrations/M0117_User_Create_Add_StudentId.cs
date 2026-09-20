using FluentMigrator;

namespace Sms.Migrations;

[Migration(117, "User_Create accepts StudentId + MustSetPassword so student creation can provision a login (embedded CREATE OR ALTER)")]
public sealed class M0117_User_Create_Add_StudentId : Migration
{
    public override void Up()
    {
        // Kept under procs/identity (not procs/saas) so M0034's broad "procs.saas."
        // EmbeddedProcs fragment doesn't pick this body up and revert it — same
        // pitfall documented in M0087/M0086.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_Create"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc body is superseded, not restored.
    }
}
