using System.Linq;
using FluentMigrator;

namespace Sms.Migrations;

[Migration(81, "Role-template get/set procs (embedded CREATE OR ALTER)")]
public sealed class M0081_Procs_RoleTemplate : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.RoleTemplate_Get")
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.RoleTemplate_Set")))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RoleTemplate_Get;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RoleTemplate_Set;");
    }
}
