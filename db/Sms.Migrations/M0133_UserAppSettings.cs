using FluentMigrator;

namespace Sms.Migrations;

[Migration(133, "UserAppSettings: per-user in-app notice preferences with tenant RLS")]
public sealed class M0133_UserAppSettings : Migration
{
    public override void Up()
    {
        Create.Table("UserAppSettings")
            .WithColumn("UserId").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ChatAlerts").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("SchoolNotices").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("InAppToasts").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_UserAppSettings_Tenant").OnTable("UserAppSettings")
            .OnColumn("TenantId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.UserAppSettingsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.UserAppSettings,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.UserAppSettings AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.UserAppSettingsTenantPolicy;");
        Delete.Table("UserAppSettings");
    }
}
