using FluentMigrator;

namespace Sms.Migrations;

[Migration(87, "User_SetPassword also clears MustSetPassword on success (embedded CREATE OR ALTER)")]
public sealed class M0087_User_SetPassword_Clear_MustSetPassword : Migration
{
    public override void Up()
    {
        // Kept under procs/identity (not procs/saas) so M0034's broad "procs.saas."
        // EmbeddedProcs fragment doesn't pick this body up and re-create it before
        // M0084 adds the MustSetPassword column — the same pitfall fixed for M0086.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_SetPassword"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc body is superseded, not restored.
    }
}
