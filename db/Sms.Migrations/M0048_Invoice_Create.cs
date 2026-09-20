using FluentMigrator;

namespace Sms.Migrations;

[Migration(48, "Catre billing: Invoice_Create proc for activation billing")]
public sealed class M0048_Invoice_Create : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catrebilling.Invoice_Create"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Invoice_Create;");
    }
}
