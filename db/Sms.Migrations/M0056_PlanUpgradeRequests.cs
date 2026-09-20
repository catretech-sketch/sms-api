using FluentMigrator;

namespace Sms.Migrations;

[Migration(56, "Plan upgrade requests: table + procs for owner Razorpay/offline + Catre approve")]
public sealed class M0056_PlanUpgradeRequests : Migration
{
    public override void Up()
    {
        Create.Table("PlanUpgradeRequests")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("FromPlanId").AsGuid().Nullable()
            .WithColumn("ToPlanId").AsGuid().NotNullable()
            .WithColumn("Amount").AsDecimal(18, 2).NotNullable()
            .WithColumn("Currency").AsString(8).NotNullable().WithDefaultValue("INR")
            .WithColumn("Mode").AsString(20).NotNullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("InvoiceId").AsGuid().Nullable()
            .WithColumn("RazorpayOrderId").AsString(80).Nullable()
            .WithColumn("RazorpayPaymentId").AsString(80).Nullable()
            .WithColumn("RequestedByUserId").AsGuid().Nullable()
            .WithColumn("ReviewedByUserId").AsGuid().Nullable()
            .WithColumn("Notes").AsString(500).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_PlanUpgradeRequests_Tenant").OnTable("PlanUpgradeRequests")
            .OnColumn("TenantId").Ascending().OnColumn("CreatedAt").Descending();
        Create.Index("IX_PlanUpgradeRequests_Status").OnTable("PlanUpgradeRequests")
            .OnColumn("Status").Ascending().OnColumn("CreatedAt").Descending();
        Create.Index("IX_PlanUpgradeRequests_RazorpayOrder").OnTable("PlanUpgradeRequests")
            .OnColumn("RazorpayOrderId").Ascending()
            .WithOptions().NonClustered();

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catrebilling.PlanUpgradeRequest_"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catrebilling.Subscription_SetPlan"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catreops.Audit_Insert"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_Get;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_List;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_ListByTenants;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_SetStatus;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_AttachRazorpay;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_AttachInvoice;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlanUpgradeRequest_GetByOrder;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Subscription_SetPlan;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Audit_Insert;");
        Delete.Table("PlanUpgradeRequests");
    }
}
