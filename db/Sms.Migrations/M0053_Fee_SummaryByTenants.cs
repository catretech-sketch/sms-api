using FluentMigrator;

namespace Sms.Migrations;

[Migration(53, "Owner portfolio: Fee_SummaryByTenants proc for cross-school fee collection")]
public sealed class M0053_Fee_SummaryByTenants : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.finance.Fee_SummaryByTenants"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Fee_SummaryByTenants;");
    }
}
