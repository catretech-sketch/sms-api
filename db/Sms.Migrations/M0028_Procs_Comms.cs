using FluentMigrator;

namespace Sms.Migrations;

[Migration(28, "Comms procs: Thread_Create, Message_Add, Announcement_Create")]
public sealed class M0028_Procs_Comms : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.comms."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Thread_Create", "Message_Add", "Announcement_Create" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
