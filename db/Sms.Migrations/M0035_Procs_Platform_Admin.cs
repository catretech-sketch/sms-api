using FluentMigrator;

namespace Sms.Migrations;

[Migration(35, "Platform admin bootstrap proc: PlatformAdmin_Exists (embedded CREATE OR ALTER)")]
public sealed class M0035_Procs_Platform_Admin : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.platformadmin."))
            Execute.Sql(sql);

        // Filtered unique index: the DB rejects a duplicate platform admin so concurrent
        // boots can't both seed one. FluentMigrator's fluent API can't express filtered
        // indexes cleanly, so use raw SQL.
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_PlatformAdmin') " +
            "CREATE UNIQUE INDEX UX_Users_PlatformAdmin ON dbo.Users(Email) WHERE IsPlatform = 1;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS UX_Users_PlatformAdmin ON dbo.Users;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlatformAdmin_Exists;");
    }
}
