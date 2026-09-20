using FluentMigrator;

namespace Sms.Migrations;

[Migration(9, "Catre Phase 1 ops: Onboarding(+checklist), Tickets(+messages), Team; AuditLog actor columns")]
public sealed class M0009_Catre_Ops_Tables : Migration
{
    public override void Up()
    {
        Create.Table("OnboardingItems")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Slug").AsString(100).NotNullable()
            .WithColumn("Owner").AsString(120).Nullable()
            .WithColumn("Value").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Stage").AsString(20).NotNullable().WithDefaultValue("lead")
            .WithColumn("Age").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("OnboardingChecklist")
            .WithColumn("OnboardingId").AsGuid().NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("Label").AsString(100).NotNullable()
            .WithColumn("Done").AsBoolean().NotNullable().WithDefaultValue(false);
        Create.PrimaryKey("PK_OnboardingChecklist").OnTable("OnboardingChecklist").Columns("OnboardingId", "Seq");

        Create.Table("Tickets")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Subject").AsString(300).NotNullable()
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("TenantName").AsString(200).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("open")
            .WithColumn("Priority").AsString(20).NotNullable().WithDefaultValue("normal")
            .WithColumn("Assignee").AsString(120).Nullable()
            .WithColumn("Created").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("Updated").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("MessagesCount").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("TicketMessages")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TicketId").AsGuid().NotNullable()
            .WithColumn("Who").AsString(120).Nullable()
            .WithColumn("Role").AsString(20).NotNullable().WithDefaultValue("agent")
            .WithColumn("Text").AsString(4000).NotNullable()
            .WithColumn("When").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_TicketMessages_Ticket").OnTable("TicketMessages").OnColumn("TicketId").Ascending();

        Create.Table("TeamMembers")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Email").AsString(256).NotNullable()
            .WithColumn("Role").AsString(20).NotNullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("invited")
            .WithColumn("LastLogin").AsDateTime2().Nullable()
            .WithColumn("Joined").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Alter.Table("AuditLog")
            .AddColumn("ActorName").AsString(200).Nullable()
            .AddColumn("Role").AsString(40).Nullable();
    }

    public override void Down()
    {
        Delete.Column("ActorName").Column("Role").FromTable("AuditLog");
        Delete.Table("TeamMembers");
        Delete.Table("TicketMessages");
        Delete.Table("Tickets");
        Delete.Table("OnboardingChecklist");
        Delete.Table("OnboardingItems");
    }
}
