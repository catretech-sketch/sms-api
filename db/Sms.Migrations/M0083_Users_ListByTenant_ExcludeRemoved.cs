using FluentMigrator;

namespace Sms.Migrations;

[Migration(83, "Users_ListByTenant: exclude 'removed' status so a deactivated person disappears from the Team tab (embedded CREATE OR ALTER)")]
public sealed class M0083_Users_ListByTenant_ExcludeRemoved : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.Users_ListByTenant"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc body (without the Status filter) is superseded, not restored.
    }
}
