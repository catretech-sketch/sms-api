using FluentMigrator;

namespace Sms.Migrations;

[Migration(57, "Catre: Client_Delete — remove empty school (no students/teachers/staff)")]
public sealed class M0057_Client_Delete : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catre.Client_Delete"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_Delete;");
    }
}
