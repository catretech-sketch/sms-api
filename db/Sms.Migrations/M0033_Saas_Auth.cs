using FluentMigrator;

namespace Sms.Migrations;

[Migration(33, "SaaS auth: generalise OtpCodes to identifier+channel; UsersTvp type for bulk import")]
public sealed class M0033_Saas_Auth : Migration
{
    public override void Up()
    {
        Alter.Table("OtpCodes")
            .AddColumn("Identifier").AsString(256).Nullable()
            .AddColumn("Channel").AsString(10).Nullable();
        Alter.Column("Phone").OnTable("OtpCodes").AsString(32).Nullable();
        Create.Index("IX_OtpCodes_Identifier").OnTable("OtpCodes").OnColumn("Identifier").Ascending();

        Execute.Sql("CREATE TYPE dbo.UsersTvp AS TABLE " +
                    "(Email nvarchar(256) NULL, Phone nvarchar(32) NULL, Role nvarchar(64) NULL);");
    }

    public override void Down()
    {
        Execute.Sql("DROP TYPE IF EXISTS dbo.UsersTvp;");
        Delete.Index("IX_OtpCodes_Identifier").OnTable("OtpCodes");
        Delete.Column("Identifier").FromTable("OtpCodes");
        Delete.Column("Channel").FromTable("OtpCodes");
    }
}
