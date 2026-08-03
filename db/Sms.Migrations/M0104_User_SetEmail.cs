using FluentMigrator;

namespace Sms.Migrations;

[Migration(104, "User_SetEmail proc — write-through so Teacher/Staff email edits update the linked Users row (login/identity email)")]
public sealed class M0104_User_SetEmail : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_SetEmail"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_SetEmail;");
    }
}
