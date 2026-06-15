using FluentMigrator;

namespace Sms.Migrations;

[Migration(5, "Catre Phase 1: Plans table + Tenants (Client) business columns")]
public sealed class M0005_Catre_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Plans")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Tier").AsString(20).NotNullable()
            .WithColumn("Pricing").AsString(20).NotNullable().WithDefaultValue("flat")
            .WithColumn("Price").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("PerStudent").AsDecimal(18, 2).Nullable()
            .WithColumn("MinStudents").AsInt32().Nullable()
            .WithColumn("Period").AsString(20).NotNullable().WithDefaultValue("month")
            .WithColumn("FeaturesCsv").AsString(4000).Nullable()
            .WithColumn("LimitsStudents").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LimitsStaff").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LimitsStorageGb").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Visibility").AsString(20).NotNullable().WithDefaultValue("draft")
            .WithColumn("Audience").AsString(20).NotNullable().WithDefaultValue("all")
            .WithColumn("Band").AsString(100).Nullable()
            .WithColumn("OfferLabel").AsString(100).Nullable()
            .WithColumn("OfferPct").AsInt32().Nullable()
            .WithColumn("Color").AsString(40).Nullable()
            .WithColumn("Description").AsString(1000).Nullable();

        Alter.Table("Tenants")
            .AddColumn("Country").AsString(120).Nullable()
            .AddColumn("PlanId").AsGuid().Nullable()
            .AddColumn("PlanName").AsString(100).Nullable()
            .AddColumn("Mrr").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("StudentsCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("StaffCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("StorageGb").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("LimitsStudents").AsInt32().Nullable()
            .AddColumn("LimitsStaff").AsInt32().Nullable()
            .AddColumn("LimitsStorageGb").AsInt32().Nullable()
            .AddColumn("ContactName").AsString(200).Nullable()
            .AddColumn("ContactEmail").AsString(256).Nullable()
            .AddColumn("ContactPhone").AsString(40).Nullable()
            .AddColumn("Csm").AsString(120).Nullable()
            .AddColumn("HealthScore").AsInt32().NotNullable().WithDefaultValue(100);
    }

    public override void Down()
    {
        Delete.Column("Country").Column("PlanId").Column("PlanName").Column("Mrr")
            .Column("StudentsCount").Column("StaffCount").Column("StorageGb")
            .Column("LimitsStudents").Column("LimitsStaff").Column("LimitsStorageGb")
            .Column("ContactName").Column("ContactEmail").Column("ContactPhone")
            .Column("Csm").Column("HealthScore").FromTable("Tenants");
        Delete.Table("Plans");
    }
}
