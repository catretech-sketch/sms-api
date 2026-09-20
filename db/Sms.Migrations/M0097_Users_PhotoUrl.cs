using FluentMigrator;

namespace Sms.Migrations;

[Migration(97, "Users.PhotoUrl for self-service profile photos (role-agnostic)")]
public sealed class M0097_Users_PhotoUrl : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Users', 'PhotoUrl') IS NULL
    ALTER TABLE dbo.Users ADD PhotoUrl nvarchar(max) NULL;
");

        // Kept under procs/identityprofile (not procs/identity) so M0086's
        // "procs.identity." EmbeddedProcs fragment doesn't pick up these bodies
        // and re-create them referencing PhotoUrl before this migration's own
        // ALTER TABLE above has run — the same ordering pitfall already fixed
        // for M0086/M0087/M0095 in this repo. procs/identity/User_GetBy*.sql
        // stay at their original (pre-CreatedAt/PhotoUrl) content so M0086
        // keeps applying cleanly on a fresh database.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityprofile.User_GetById"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityprofile.User_GetByEmail"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityprofile.User_GetByPhone"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Users', 'PhotoUrl') IS NOT NULL
    ALTER TABLE dbo.Users DROP COLUMN PhotoUrl;
");
    }
}
