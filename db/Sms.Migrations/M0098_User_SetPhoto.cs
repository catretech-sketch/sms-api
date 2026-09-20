using FluentMigrator;

namespace Sms.Migrations;

[Migration(98, "User_SetPhoto proc for self-service profile photo updates")]
public sealed class M0098_User_SetPhoto : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identity.User_SetPhoto"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_SetPhoto;");
    }
}
