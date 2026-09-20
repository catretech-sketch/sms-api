using FluentMigrator;

namespace Sms.Migrations;

[Migration(82, "Users: filtered unique index on (TenantId, Email) and (TenantId, Phone) — stop duplicate invites creating repeat rows for the same person in a school")]
public sealed class M0082_Users_Tenant_Email_Unique : Migration
{
    public override void Up()
    {
        // Filtered unique index: FluentMigrator's fluent API can't express filtered
        // indexes, so use raw SQL (same approach as UX_Users_PlatformAdmin, M0035).
        // NULL is excluded via the WHERE filter, since phone-only or email-only
        // accounts leave the other column NULL and must not collide on that.
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_Tenant_Email') " +
            "CREATE UNIQUE INDEX UX_Users_Tenant_Email ON dbo.Users(TenantId, Email) WHERE Email IS NOT NULL;");
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_Tenant_Phone') " +
            "CREATE UNIQUE INDEX UX_Users_Tenant_Phone ON dbo.Users(TenantId, Phone) WHERE Phone IS NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS UX_Users_Tenant_Email ON dbo.Users;");
        Execute.Sql("DROP INDEX IF EXISTS UX_Users_Tenant_Phone ON dbo.Users;");
    }
}
