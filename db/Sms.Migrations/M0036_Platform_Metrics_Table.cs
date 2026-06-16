using FluentMigrator;

namespace Sms.Migrations;

[Migration(36, "Platform metrics snapshot: monthly MRR/active/cancelled for trend + churn")]
public sealed class M0036_Platform_Metrics_Table : Migration
{
    public override void Up()
    {
        Create.Table("PlatformMetricsSnapshot")
            .WithColumn("Month").AsDate().PrimaryKey()                 // first-of-month (UTC)
            .WithColumn("Mrr").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("ActiveClients").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CancelledClients").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down() => Delete.Table("PlatformMetricsSnapshot");
}
