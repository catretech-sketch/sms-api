using FluentMigrator;

namespace Sms.Migrations;

[Migration(21, "Geofenced check-in: SchoolLocations + CheckIns with tenant RLS")]
public sealed class M0021_Geofence_Tables : Migration
{
    public override void Up()
    {
        Create.Table("SchoolLocations")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable().Unique()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("RadiusMeters").AsInt32().NotNullable().WithDefaultValue(50)
            .WithColumn("Name").AsString(120).Nullable();

        Create.Table("CheckIns")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Kind").AsString(3).NotNullable()
            .WithColumn("At").AsDateTime2().NotNullable()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("AccuracyMeters").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("DistanceMeters").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Verified").AsBoolean().NotNullable().WithDefaultValue(false);
        Create.Index("IX_CheckIns_User_At").OnTable("CheckIns")
            .OnColumn("UserId").Ascending().OnColumn("At").Ascending();

        foreach (var t in new[] { "SchoolLocations", "CheckIns" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.CheckInsTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.SchoolLocationsTenantPolicy;");
        Delete.Table("CheckIns");
        Delete.Table("SchoolLocations");
    }
}
