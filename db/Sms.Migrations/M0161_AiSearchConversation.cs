using FluentMigrator;

namespace Sms.Migrations;

[Migration(161, "AiSearchConversation: short-lived conversational context for AI person lookup follow-ups")]
public sealed class M0161_AiSearchConversation : Migration
{
    public override void Up()
    {
        Create.Table("AiSearchConversation")
            .WithColumn("ConversationId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("ResolvedEntityId").AsGuid().Nullable()
            .WithColumn("ResolvedEntityType").AsString(20).Nullable()
            .WithColumn("LanguageOverride").AsString(10).Nullable()
            .WithColumn("PendingCandidates").AsString(int.MaxValue).Nullable()
            .WithColumn("LastIntent").AsString(60).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable();

        Create.Index("IX_AiSearchConversation_Expiry")
            .OnTable("AiSearchConversation")
            .OnColumn("ExpiresAt").Ascending();
    }

    public override void Down()
    {
        Delete.Table("AiSearchConversation");
    }
}
