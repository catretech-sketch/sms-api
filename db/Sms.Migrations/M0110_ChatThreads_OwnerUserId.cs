using FluentMigrator;

namespace Sms.Migrations;

[Migration(110, "ChatThreads: OwnerUserId for per-user inbox")]
public sealed class M0110_ChatThreads_OwnerUserId : Migration
{
    public override void Up()
    {
        Alter.Table("ChatThreads")
            .AddColumn("OwnerUserId").AsGuid().Nullable();

        Create.Index("IX_ChatThreads_Owner")
            .OnTable("ChatThreads")
            .OnColumn("TenantId").Ascending()
            .OnColumn("OwnerUserId").Ascending()
            .OnColumn("Name").Ascending();

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.comms.Thread_Create"))
            Execute.Sql(sql);

        // Remove legacy school-wide shared threads (no owner) so each inbox is private.
        Execute.Sql(@"
DELETE m FROM dbo.ChatMessages m
INNER JOIN dbo.ChatThreads t ON t.Id = m.ThreadId
WHERE t.OwnerUserId IS NULL;
DELETE FROM dbo.ChatThreads WHERE OwnerUserId IS NULL;");
    }

    public override void Down()
    {
        Delete.Index("IX_ChatThreads_Owner").OnTable("ChatThreads");
        Delete.Column("OwnerUserId").FromTable("ChatThreads");
    }
}
