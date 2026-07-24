using FluentMigrator;

namespace Sms.Migrations;

[Migration(69, "Academics: Subject_Update proc (edit subject name)")]
public sealed class M0069_Subject_Update : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.academics.Subject_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Subject_Update;");
    }
}
