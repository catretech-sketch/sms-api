using FluentMigrator;

namespace Sms.Migrations;

[Migration(162, "Users: add (TenantId, Name) index for AI person-lookup search - Users.Name column already exists since M0084")]
public sealed class M0162_Users_TenantNameIndex : Migration
{
    public override void Up()
    {
        Create.Index("IX_Users_Tenant_Name")
            .OnTable("Users")
            .OnColumn("TenantId").Ascending()
            .OnColumn("Name").Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_Users_Tenant_Name").OnTable("Users");
    }
}
