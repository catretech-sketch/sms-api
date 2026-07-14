using FluentMigrator;

namespace Sms.Migrations;

[Migration(52, "Auth login procs: User_GetByEmail / User_GetByPhone always return at most one row")]
public sealed class M0052_Auth_Login_Single_Row : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.auth.User_GetByEmail"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.User_GetByPhone"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // Idempotent CREATE OR ALTER — no destructive down.
    }
}
