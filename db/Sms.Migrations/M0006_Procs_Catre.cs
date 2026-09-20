using FluentMigrator;

namespace Sms.Migrations;

[Migration(6, "Catre Phase 1 procs: Plan_Upsert + Client_Create/SetStatus/ChangePlan (embedded CREATE OR ALTER)")]
public sealed class M0006_Procs_Catre : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catre."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Plan_Upsert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_SetStatus;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_ChangePlan;");
    }
}
