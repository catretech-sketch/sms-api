using FluentMigrator;

namespace Sms.Migrations;

[Migration(68, "Academics: Class_Update proc (edit room / class details)")]
public sealed class M0068_Class_Update : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.academics.Class_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Class_Update;");
    }
}
