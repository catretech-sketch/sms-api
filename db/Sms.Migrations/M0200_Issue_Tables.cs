using FluentMigrator;

namespace Sms.Migrations;

[Migration(200, "Issues: Issues + IssueNotes tables with tenant RLS")]
public sealed class M0200_Issue_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Issues")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ReporterUserId").AsGuid().NotNullable()
            .WithColumn("Category").AsString(20).NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Description").AsString(2000).NotNullable()
            .WithColumn("Priority").AsString(20).NotNullable().WithDefaultValue("normal")
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("open")
            .WithColumn("VehicleId").AsGuid().Nullable()
            .WithColumn("RouteId").AsGuid().Nullable()
            .WithColumn("TripId").AsGuid().Nullable()
            .WithColumn("PhotoUrl").AsString(int.MaxValue).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Issues_Tenant").OnTable("Issues").OnColumn("TenantId").Ascending();
        Create.Index("IX_Issues_Reporter").OnTable("Issues").OnColumn("ReporterUserId").Ascending();
        Create.Index("IX_Issues_Status").OnTable("Issues").OnColumn("Status").Ascending();
        Create.Index("IX_Issues_CreatedAt").OnTable("Issues").OnColumn("CreatedAt").Descending();

        Create.Table("IssueNotes")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("IssueId").AsGuid().NotNullable()
            .WithColumn("AuthorUserId").AsGuid().NotNullable()
            .WithColumn("Note").AsString(1000).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_IssueNotes_Issue").OnTable("IssueNotes").OnColumn("IssueId").Ascending();

        foreach (var t in new[] { "Issues", "IssueNotes" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "IssueNotes", "Issues" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("IssueNotes");
        Delete.Table("Issues");
    }
}
