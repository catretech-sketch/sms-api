using FluentMigrator;

namespace Sms.Migrations;

[Migration(34, "SaaS procs: OTP (identifier), phone lookup, user create/roles/set-password, bulk create, tenant tier/status")]
public sealed class M0034_Procs_Saas : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[]
        {
            "User_GetByPhone", "Otp_Consume", "User_Create", "UserRole_Add",
            "User_SetPassword", "Tenant_GetTierAndStatus", "Users_BulkCreate"
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
