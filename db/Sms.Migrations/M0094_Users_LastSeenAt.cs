using FluentMigrator;

namespace Sms.Migrations;

[Migration(94, "Users: add LastSeenAt for polling-based chat presence")]
public sealed class M0094_Users_LastSeenAt : Migration
{
    public override void Up() =>
        Alter.Table("Users").AddColumn("LastSeenAt").AsDateTime2().Nullable();

    public override void Down() =>
        Delete.Column("LastSeenAt").FromTable("Users");
}
