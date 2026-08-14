using FluentMigrator;

namespace Sms.Migrations;

[Migration(131, "Notifications: optional UserId so chat alerts are recipient-only")]
public sealed class M0131_Notifications_UserId : Migration
{
    private const string NotificationCreate = """
        CREATE OR ALTER PROCEDURE dbo.Notification_Create
            @TenantId uniqueidentifier,
            @Icon nvarchar(40) = NULL,
            @Tone nvarchar(20) = NULL,
            @Title nvarchar(200),
            @Body nvarchar(1000) = NULL,
            @UserId uniqueidentifier = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Id uniqueidentifier = NEWID();
            INSERT dbo.Notifications (Id, TenantId, Icon, Tone, Title, Body, [Time], Unread, UserId)
            VALUES (@Id, @TenantId, @Icon, @Tone, @Title, @Body, CONVERT(varchar(8), SYSUTCDATETIME(), 108), 1, @UserId);

            SELECT Id, TenantId, Icon, Tone, Title, Body, [Time], Unread
            FROM dbo.Notifications WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        if (!Schema.Table("Notifications").Column("UserId").Exists())
        {
            Alter.Table("Notifications")
                .AddColumn("UserId").AsGuid().Nullable();
        }

        Execute.Sql(NotificationCreate);
    }

    public override void Down()
    {
        Execute.Sql("""
            CREATE OR ALTER PROCEDURE dbo.Notification_Create
                @TenantId uniqueidentifier, @Icon nvarchar(40), @Tone nvarchar(20), @Title nvarchar(200), @Body nvarchar(1000)
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Id uniqueidentifier = NEWID();
                INSERT dbo.Notifications (Id, TenantId, Icon, Tone, Title, Body)
                VALUES (@Id, @TenantId, @Icon, @Tone, @Title, @Body);
                SELECT Id, TenantId, Icon, Tone, Title, Body, [Time], Unread FROM dbo.Notifications WHERE Id = @Id;
            END
            """);

        if (Schema.Table("Notifications").Column("UserId").Exists())
            Delete.Column("UserId").FromTable("Notifications");
    }
}
