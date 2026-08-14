using FluentMigrator;

namespace Sms.Migrations;

[Migration(137, "Parent_EnsureLogin: do not copy guardian phone onto parent when another login already owns it")]
public sealed class M0137_ParentEnsureLogin_SkipCollidingPhone : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
    }
}
