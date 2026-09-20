using FluentMigrator;

namespace Sms.Migrations;

[Migration(203, "Tasks procs: Task_Create, Task_Complete, Task_AttachPhoto")]
public sealed class M0203_Procs_Tasks : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.tasks."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Task_Create", "Task_Complete", "Task_AttachPhoto" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
