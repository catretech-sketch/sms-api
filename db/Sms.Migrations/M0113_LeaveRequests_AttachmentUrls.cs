using FluentMigrator;

namespace Sms.Migrations;

[Migration(113, "LeaveRequests: AttachmentUrls + Leave_Create proc")]
public sealed class M0113_LeaveRequests_AttachmentUrls : Migration
{
    public override void Up()
    {
        if (!Schema.Table("LeaveRequests").Column("AttachmentUrls").Exists())
            Alter.Table("LeaveRequests")
                .AddColumn("AttachmentUrls").AsString(int.MaxValue).Nullable();

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leaveidentity.Leave_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.leaveidentity.Leave_Decide"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        if (Schema.Table("LeaveRequests").Column("AttachmentUrls").Exists())
            Delete.Column("AttachmentUrls").FromTable("LeaveRequests");
    }
}
