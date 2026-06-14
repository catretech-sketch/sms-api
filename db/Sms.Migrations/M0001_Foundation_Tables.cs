using FluentMigrator;

namespace Sms.Migrations;

[Migration(1, "Foundation tables: tenants, users, roles, refresh tokens, otp, audit")]
public sealed class M0001_Foundation_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Tenants")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Slug").AsString(100).NotNullable().Unique()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("trial")
            .WithColumn("Tier").AsString(20).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Table("Users")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("StudentId").AsString(64).Nullable()
            .WithColumn("Phone").AsString(32).Nullable()
            .WithColumn("PasswordHash").AsString(512).Nullable()
            .WithColumn("IsPlatform").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Users_Tenant_Email").OnTable("Users")
            .OnColumn("TenantId").Ascending().OnColumn("Email").Ascending();

        Create.Table("UserRoles")
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("Role").AsString(64).NotNullable();
        Create.PrimaryKey("PK_UserRoles").OnTable("UserRoles").Columns("UserId", "Role");

        Create.Table("RefreshTokens")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("UserId").AsGuid().NotNullable()
            .WithColumn("TokenHash").AsString(128).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("RevokedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_RefreshTokens_Hash").OnTable("RefreshTokens").OnColumn("TokenHash").Ascending();

        Create.Table("OtpCodes")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("Phone").AsString(32).NotNullable()
            .WithColumn("CodeHash").AsString(128).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("ConsumedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Table("AuditLog")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("ActorId").AsGuid().Nullable()
            .WithColumn("Action").AsString(128).NotNullable()
            .WithColumn("Target").AsString(256).Nullable()
            .WithColumn("Kind").AsString(64).Nullable()
            .WithColumn("At").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down()
    {
        Delete.Table("AuditLog");
        Delete.Table("OtpCodes");
        Delete.Table("RefreshTokens");
        Delete.Table("UserRoles");
        Delete.Table("Users");
        Delete.Table("Tenants");
    }
}
