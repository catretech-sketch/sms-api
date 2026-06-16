using FluentMigrator;

namespace Sms.Migrations;

[Migration(35, "Platform admin bootstrap proc: PlatformAdmin_Exists (embedded CREATE OR ALTER)")]
public sealed class M0035_Procs_Platform_Admin : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.platformadmin."))
            Execute.Sql(sql);
    }

    public override void Down()
        => Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlatformAdmin_Exists;");
}
