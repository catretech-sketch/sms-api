using FluentMigrator;

namespace Sms.Migrations;

[Migration(8, "Catre Phase 1 billing procs: Invoice_MarkPaid/Refund, Subscription_Create, Dashboard_CatreOverview")]
public sealed class M0008_Procs_Catre_Billing : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catrebilling."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Invoice_MarkPaid;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Invoice_Refund;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Subscription_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Dashboard_CatreOverview;");
    }
}
