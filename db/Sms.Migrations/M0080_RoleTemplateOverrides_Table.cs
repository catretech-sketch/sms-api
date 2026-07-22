using FluentMigrator;

namespace Sms.Migrations;

[Migration(80, "Role-permission template overrides, tenant-scoped")]
public sealed class M0080_RoleTemplateOverrides_Table : Migration
{
    public override void Up()
    {
        Create.Table("RoleTemplateOverrides")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Role").AsString(32).NotNullable()
            .WithColumn("Module").AsString(64).NotNullable()
            .WithColumn("Cap").AsString(1).NotNullable()
            .WithColumn("Effect").AsString(8).NotNullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedByUserId").AsGuid().Nullable();

        Create.Index("IX_RoleTemplateOverrides_Tenant")
            .OnTable("RoleTemplateOverrides").OnColumn("TenantId").Ascending();

        Execute.Sql(
            "CREATE UNIQUE INDEX UX_RoleTemplateOverrides_Cell ON dbo.RoleTemplateOverrides " +
            "(TenantId, Role, Module, Cap);");

        Execute.Sql(
            "CREATE SECURITY POLICY rls.RoleTemplateOverridesTenantPolicy " +
            "ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RoleTemplateOverrides, " +
            "ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RoleTemplateOverrides AFTER INSERT " +
            "WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.RoleTemplateOverridesTenantPolicy;");
        Delete.Table("RoleTemplateOverrides");
    }
}
