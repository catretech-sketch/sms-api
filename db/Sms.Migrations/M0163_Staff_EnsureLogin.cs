using FluentMigrator;

namespace Sms.Migrations;

[Migration(163, "Staff_EnsureLogin: create staff login from dbo.Staff.Email when missing (OTP self-serve, no invite)")]
public sealed class M0163_Staff_EnsureLogin : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Staff_EnsureLogin"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_EnsureLogin;");
    }
}
