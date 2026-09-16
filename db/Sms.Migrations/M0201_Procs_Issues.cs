using FluentMigrator;

namespace Sms.Migrations;

[Migration(201, "Issues procs: Issue_Create/Update, IssueNote_Add")]
public sealed class M0201_Procs_Issues : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.issues."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Issue_Create", "Issue_Update", "IssueNote_Add" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
