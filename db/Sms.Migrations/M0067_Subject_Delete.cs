using FluentMigrator;

namespace Sms.Migrations;

[Migration(67, "Academics: Subject_Delete proc")]
public sealed class M0067_Subject_Delete : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.academics.Subject_Delete"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Subject_Delete;");
    }
}
