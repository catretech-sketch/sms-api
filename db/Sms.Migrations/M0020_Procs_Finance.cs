using FluentMigrator;

namespace Sms.Migrations;

[Migration(20, "Finance procs: FeePayment_Create (embedded CREATE OR ALTER)")]
public sealed class M0020_Procs_Finance : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.finance."))
            Execute.Sql(sql);
    }

    public override void Down() => Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeePayment_Create;");
}
