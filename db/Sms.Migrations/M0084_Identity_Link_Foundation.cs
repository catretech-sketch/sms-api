using FluentMigrator;

namespace Sms.Migrations;

[Migration(84, "Identity-link foundation: Users.Name/MustSetPassword, Teachers/Staff.UserId")]
public sealed class M0084_Identity_Link_Foundation : Migration
{
    public override void Up()
    {
        Alter.Table("Users")
            .AddColumn("Name").AsString(200).Nullable()
            .AddColumn("MustSetPassword").AsBoolean().NotNullable().WithDefaultValue(false);

        Alter.Table("Teachers").AddColumn("UserId").AsGuid().Nullable();
        Alter.Table("Staff").AddColumn("UserId").AsGuid().Nullable();

        Execute.Sql(
            "CREATE UNIQUE INDEX IX_Teachers_UserId ON dbo.Teachers (UserId) WHERE UserId IS NOT NULL;");
        Execute.Sql(
            "CREATE UNIQUE INDEX IX_Staff_UserId ON dbo.Staff (UserId) WHERE UserId IS NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS IX_Staff_UserId ON dbo.Staff;");
        Execute.Sql("DROP INDEX IF EXISTS IX_Teachers_UserId ON dbo.Teachers;");
        Delete.Column("UserId").FromTable("Staff");
        Delete.Column("UserId").FromTable("Teachers");
        Delete.Column("MustSetPassword").FromTable("Users");
        Delete.Column("Name").FromTable("Users");
    }
}
