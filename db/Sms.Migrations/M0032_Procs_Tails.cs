using FluentMigrator;

namespace Sms.Migrations;

[Migration(32, "Tails procs: Complaint_Create/Update, Notification_Create, Payslip_Create")]
public sealed class M0032_Procs_Tails : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.tails."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Complaint_Create", "Complaint_Update", "Notification_Create", "Payslip_Create" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
