using FluentMigrator;

namespace Sms.Migrations;

[Migration(45, "School owner role: convert existing founding school.admin rows to school.owner")]
public sealed class M0045_School_Owner_Role : Migration
{
    public override void Up() =>
        Execute.Sql("UPDATE dbo.UserRoles SET Role = N'school.owner' WHERE Role = N'school.admin';");

    public override void Down() =>
        Execute.Sql("UPDATE dbo.UserRoles SET Role = N'school.admin' WHERE Role = N'school.owner';");
}
