using FluentMigrator;

namespace Sms.Migrations;

[Migration(207, "RouteGeometries: cached road-following geometry per transport route")]
public sealed class M0207_RouteGeometries : Migration
{
    public override void Up()
    {
        Create.Table("RouteGeometries")
            .WithColumn("RouteId").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StopSequenceHash").AsString(64).NotNullable()
            .WithColumn("Format").AsString(32).NotNullable()
            .WithColumn("EncodedPolyline").AsCustom("nvarchar(max)").NotNullable()
            .WithColumn("DistanceMeters").AsInt32().NotNullable()
            .WithColumn("DurationSeconds").AsInt32().NotNullable()
            .WithColumn("Provider").AsString(32).NotNullable()
            .WithColumn("GeneratedAt").AsDateTime2().NotNullable();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.RouteGeometriesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RouteGeometries,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RouteGeometries AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.RouteGeometriesTenantPolicy;");
        Delete.Table("RouteGeometries");
    }
}
