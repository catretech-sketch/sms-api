using FluentMigrator;

namespace Sms.Migrations;

[Migration(26, "Phase 5 procs: Homework_Create/SetStatus, FeeInvoice_Create/MarkPaid")]
public sealed class M0026_Procs_Phase5 : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.phase5."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Homework_Create", "Homework_SetStatus", "FeeInvoice_Create", "FeeInvoice_MarkPaid" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
