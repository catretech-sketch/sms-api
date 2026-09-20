using FluentMigrator;

namespace Sms.Migrations;

[Migration(132, "Achievements: staff awards per student with tenant RLS")]
public sealed class M0132_Achievements : Migration
{
    public override void Up()
    {
        Create.Table("Achievements")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("AwardedOn").AsDate().NotNullable()
            .WithColumn("Icon").AsString(20).NotNullable().WithDefaultValue("award")
            .WithColumn("Hue").AsString(20).NotNullable().WithDefaultValue("yellow")
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_Achievements_Tenant_Student").OnTable("Achievements")
            .OnColumn("TenantId").Ascending()
            .OnColumn("StudentId").Ascending()
            .OnColumn("AwardedOn").Descending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.AchievementsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Achievements,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Achievements AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AchievementsTenantPolicy;");
        Delete.Table("Achievements");
    }
}
