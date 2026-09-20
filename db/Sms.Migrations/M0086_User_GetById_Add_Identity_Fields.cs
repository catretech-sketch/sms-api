using FluentMigrator;

namespace Sms.Migrations;

[Migration(86, "Identity fields on User_GetById/GetByEmail/GetByPhone (embedded CREATE OR ALTER)")]
public sealed class M0086_User_GetById_Add_Identity_Fields : Migration
{
    public override void Up()
    {
        // Kept under procs/identity (not procs/auth, procs/authlogin, or procs/saas) so the
        // broad-prefix EmbeddedProcs fragments used by M0003/M0004/M0034 (and M0052's
        // "procs.auth.User_GetByEmail"/"procs.saas.User_GetByPhone") don't pick up this body
        // and re-create it — with Name/MustSetPassword columns — before M0084 adds them.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_GetById"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_GetByEmail"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_GetByPhone"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc bodies are superseded, not restored.
    }
}
