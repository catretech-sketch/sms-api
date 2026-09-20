using FluentMigrator;

namespace Sms.Migrations;

[Migration(206, "BusParentAlerts: one trip_started / approaching_stop notice per student per trip")]
public sealed class M0206_BusParentAlerts : Migration
{
    public override void Up()
    {
        Create.Table("BusParentAlerts")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TripId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("ParentUserId").AsGuid().NotNullable()
            .WithColumn("Kind").AsString(40).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("UX_BusParentAlerts_Trip_Student_Parent_Kind").OnTable("BusParentAlerts")
            .OnColumn("TripId").Ascending()
            .OnColumn("StudentId").Ascending()
            .OnColumn("ParentUserId").Ascending()
            .OnColumn("Kind").Ascending()
            .WithOptions().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.BusParentAlertsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusParentAlerts,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusParentAlerts AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.BusParentAlertsTenantPolicy;");
        Delete.Table("BusParentAlerts");
    }
}
