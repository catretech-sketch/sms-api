using FluentMigrator;

namespace Sms.Migrations;

[Migration(92, "LeaveRequests: add Priority column (mirrors Complaints.Priority)")]
public sealed class M0092_LeaveRequests_Priority : Migration
{
    public override void Up() =>
        Alter.Table("LeaveRequests").AddColumn("Priority").AsString(10).NotNullable().WithDefaultValue("medium");

    public override void Down() =>
        Delete.Column("Priority").FromTable("LeaveRequests");
}
