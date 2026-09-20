using FluentMigrator;

namespace Sms.Migrations;

[Migration(88, "Announcements: add CreatorUserId for read-time creator-name resolution")]
public sealed class M0088_Announcements_CreatorUserId : Migration
{
    public override void Up() =>
        Alter.Table("Announcements").AddColumn("CreatorUserId").AsGuid().Nullable();

    public override void Down() =>
        Delete.Column("CreatorUserId").FromTable("Announcements");
}
