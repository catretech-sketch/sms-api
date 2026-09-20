using FluentMigrator;

namespace Sms.Migrations;

[Migration(57, "Catre: Client_Delete — remove empty school (no students/teachers/staff)")]
public sealed class M0057_Client_Delete : Migration
{
    public override void Up()
    {
        // Keep under procs/catredel (not procs/catre) so M0006's EmbeddedProcs("procs.catre.")
        // does not create this proc before PlanUpgradeRequests and other late tables exist.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catredel.Client_Delete"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_Delete;");
    }
}
