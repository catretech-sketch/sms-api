using FluentMigrator;

namespace Sms.Migrations;

[Migration(189, "Razorpay online fee payment: TenantPaymentCredentials + FeePaymentOrders")]
public sealed class M0180_Razorpay_Fee_Payment_Schema : Migration
{
    public override void Up()
    {
        Create.Table("TenantPaymentCredentials")
            .WithColumn("TenantId").AsGuid().PrimaryKey()
            .WithColumn("Provider").AsString(20).NotNullable().WithDefaultValue("razorpay")
            .WithColumn("KeyId").AsString(100).Nullable()
            .WithColumn("KeySecretEncrypted").AsCustom("nvarchar(max)").Nullable()
            .WithColumn("WebhookSecretEncrypted").AsCustom("nvarchar(max)").Nullable()
            .WithColumn("Mode").AsString(10).NotNullable().WithDefaultValue("test")
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        Create.Table("FeePaymentOrders")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("InvoiceId").AsGuid().NotNullable()
            .WithColumn("RazorpayOrderId").AsString(100).NotNullable()
            .WithColumn("AmountPaise").AsInt64().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("Created")
            .WithColumn("InitiatedBy").AsString(20).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        Create.Index("UX_FeePaymentOrders_RazorpayOrderId")
            .OnTable("FeePaymentOrders")
            .OnColumn("RazorpayOrderId").Ascending()
            .WithOptions().Unique();

        Create.Index("IX_FeePaymentOrders_Tenant_Invoice")
            .OnTable("FeePaymentOrders")
            .OnColumn("TenantId").Ascending()
            .OnColumn("InvoiceId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("FeePaymentOrders");
        Delete.Table("TenantPaymentCredentials");
    }
}
