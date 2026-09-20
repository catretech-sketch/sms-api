using FluentMigrator;

namespace Sms.Migrations;

[Migration(7, "Catre Phase 1 billing: Invoices + Subscriptions")]
public sealed class M0007_Catre_Billing_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Invoices")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("TenantName").AsString(200).Nullable()
            .WithColumn("PlanName").AsString(100).Nullable()
            .WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("open")
            .WithColumn("Issued").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("Due").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("PaidOn").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Invoices_Tenant").OnTable("Invoices").OnColumn("TenantId").Ascending();

        Create.Table("Subscriptions")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("PlanId").AsGuid().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("StartedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("RenewsAt").AsDateTime2().Nullable()
            .WithColumn("Seats").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Index("IX_Subscriptions_Tenant").OnTable("Subscriptions").OnColumn("TenantId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("Subscriptions");
        Delete.Table("Invoices");
    }
}
