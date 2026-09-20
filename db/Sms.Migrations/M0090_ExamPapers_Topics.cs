using FluentMigrator;

namespace Sms.Migrations;

[Migration(90, "ExamPapers: add Topics column")]
public sealed class M0090_ExamPapers_Topics : Migration
{
    public override void Up() =>
        Alter.Table("ExamPapers").AddColumn("Topics").AsString(int.MaxValue).Nullable();

    public override void Down() =>
        Delete.Column("Topics").FromTable("ExamPapers");
}
