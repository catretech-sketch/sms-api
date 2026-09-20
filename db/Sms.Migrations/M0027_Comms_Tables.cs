using FluentMigrator;

namespace Sms.Migrations;

[Migration(27, "Comms: ChatThreads, ChatMessages, Announcements with tenant RLS")]
public sealed class M0027_Comms_Tables : Migration
{
    public override void Up()
    {
        Create.Table("ChatThreads")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Role").AsString(40).Nullable()
            .WithColumn("LastMessage").AsString(400).Nullable()
            .WithColumn("LastAt").AsDateTime2().Nullable()
            .WithColumn("Unread").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("IsGroup").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("ChildId").AsGuid().Nullable();
        Create.Index("IX_ChatThreads_Tenant").OnTable("ChatThreads").OnColumn("TenantId").Ascending();

        Create.Table("ChatMessages")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ThreadId").AsGuid().NotNullable()
            .WithColumn("SenderId").AsGuid().Nullable()
            .WithColumn("Text").AsString(2000).NotNullable()
            .WithColumn("SentAt").AsDateTime2().NotNullable();
        Create.Index("IX_ChatMessages_Thread").OnTable("ChatMessages").OnColumn("ThreadId").Ascending();

        Create.Table("Announcements")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Body").AsString(2000).Nullable()
            .WithColumn("Date").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("From").AsString(120).Nullable()
            .WithColumn("Role").AsString(40).Nullable()
            .WithColumn("Type").AsString(20).NotNullable().WithDefaultValue("info")
            .WithColumn("Pinned").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Audience").AsString(20).Nullable();
        Create.Index("IX_Announcements_Tenant").OnTable("Announcements").OnColumn("TenantId").Ascending();

        foreach (var t in new[] { "ChatThreads", "ChatMessages", "Announcements" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "ChatThreads", "ChatMessages", "Announcements" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("Announcements");
        Delete.Table("ChatMessages");
        Delete.Table("ChatThreads");
    }
}
