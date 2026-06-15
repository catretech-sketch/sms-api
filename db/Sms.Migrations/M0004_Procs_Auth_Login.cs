using FluentMigrator;

namespace Sms.Migrations;

[Migration(4, "Auth login procs: User_GetById + UserRoles_GetByUser (embedded CREATE OR ALTER)")]
public sealed class M0004_Procs_Auth_Login : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.authlogin."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_GetById;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.UserRoles_GetByUser;");
    }
}
