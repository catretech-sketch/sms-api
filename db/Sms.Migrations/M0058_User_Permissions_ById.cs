using System.Linq;
using FluentMigrator;

namespace Sms.Migrations;

[Migration(58, "Per-user roles & permission overrides by user id (tenant-scoped)")]
public sealed class M0058_User_Permissions_ById : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPermissions (
        UserId  uniqueidentifier NOT NULL,
        Module  nvarchar(64)     NOT NULL,
        Cap     char(1)          NOT NULL,
        Effect  nvarchar(16)     NOT NULL,
        CONSTRAINT PK_UserPermissions PRIMARY KEY (UserId, Module, Cap),
        CONSTRAINT CK_UserPermissions_Cap CHECK (Cap IN ('V','E','A')),
        CONSTRAINT CK_UserPermissions_Effect CHECK (Effect IN ('grant','revoke')),
        CONSTRAINT FK_UserPermissions_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
    );
END
");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.Users_ListByTenant")
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.UserPermissions_Get"))
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.UserPermissions_Set"))
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.UserRoles_Replace")))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[]
        {
            "Users_ListByTenant", "UserPermissions_Get", "UserPermissions_Set", "UserRoles_Replace",
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
        Execute.Sql("DROP TABLE IF EXISTS dbo.UserPermissions;");
    }
}
