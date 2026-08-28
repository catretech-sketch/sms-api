using FluentMigrator;

namespace Sms.Migrations;

[Migration(160, "AiSearchLog: audit trail for AI global search queries")]
public sealed class M0160_AiSearchLog : Migration
{
    public override void Up()
    {
        Create.Table("AiSearchLog")
            .WithColumn("Id").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Role").AsString(100).NotNullable()
            .WithColumn("Question").AsString(300).NotNullable()
            .WithColumn("DetectedLanguage").AsString(20).Nullable()
            .WithColumn("DetectedIntent").AsString(60).Nullable()
            .WithColumn("ResultCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Success").AsBoolean().NotNullable()
            .WithColumn("At").AsDateTime2().NotNullable();

        Create.Index("IX_AiSearchLog_Tenant_At")
            .OnTable("AiSearchLog")
            .OnColumn("TenantId").Ascending()
            .OnColumn("At").Descending();
    }

    public override void Down()
    {
        Delete.Table("AiSearchLog");
    }
}
