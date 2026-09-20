using FluentMigrator;

namespace Sms.Migrations;

[Migration(130, "ChatMessages: CorrelationId + delivered/read receipts; Message_Add")]
public sealed class M0130_ChatMessages_ReadReceipts : Migration
{
    private const string MessageAddWithReceipts = """
        CREATE OR ALTER PROCEDURE dbo.Message_Add
            @TenantId uniqueidentifier,
            @ThreadId uniqueidentifier,
            @SenderId uniqueidentifier,
            @Text nvarchar(2000) = NULL,
            @ImageUrl nvarchar(max) = NULL,
            @CorrelationId uniqueidentifier = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            IF @Text IS NULL SET @Text = N'';
            IF @CorrelationId IS NULL SET @CorrelationId = NEWID();
            DECLARE @Id uniqueidentifier = NEWID();
            DECLARE @Now datetime2 = SYSUTCDATETIME();
            DECLARE @Preview nvarchar(400) = CASE
                WHEN LEN(LTRIM(RTRIM(@Text))) > 0 THEN LEFT(@Text, 400)
                WHEN @ImageUrl IS NOT NULL THEN N'[Image]'
                ELSE N''
            END;

            INSERT dbo.ChatMessages (Id, TenantId, ThreadId, SenderId, [Text], ImageUrl, SentAt, CorrelationId, DeliveredAt, ReadAt)
            VALUES (@Id, @TenantId, @ThreadId, @SenderId, @Text, @ImageUrl, @Now, @CorrelationId, NULL, NULL);

            UPDATE dbo.ChatThreads
            SET LastMessage = @Preview, LastAt = @Now
            WHERE Id = @ThreadId;

            SELECT Id, ThreadId, SenderId, [Text], ImageUrl, SentAt, DeliveredAt, ReadAt
            FROM dbo.ChatMessages WHERE Id = @Id;
        END
        """;

    private const string MessageAddWithoutReceipts = """
        CREATE OR ALTER PROCEDURE dbo.Message_Add
            @TenantId uniqueidentifier,
            @ThreadId uniqueidentifier,
            @SenderId uniqueidentifier,
            @Text nvarchar(2000) = NULL,
            @ImageUrl nvarchar(max) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            IF @Text IS NULL SET @Text = N'';
            DECLARE @Id uniqueidentifier = NEWID();
            DECLARE @Now datetime2 = SYSUTCDATETIME();
            DECLARE @Preview nvarchar(400) = CASE
                WHEN LEN(LTRIM(RTRIM(@Text))) > 0 THEN LEFT(@Text, 400)
                WHEN @ImageUrl IS NOT NULL THEN N'[Image]'
                ELSE N''
            END;

            INSERT dbo.ChatMessages (Id, TenantId, ThreadId, SenderId, [Text], ImageUrl, SentAt)
            VALUES (@Id, @TenantId, @ThreadId, @SenderId, @Text, @ImageUrl, @Now);

            UPDATE dbo.ChatThreads
            SET LastMessage = @Preview, LastAt = @Now
            WHERE Id = @ThreadId;

            SELECT Id, ThreadId, SenderId, [Text], ImageUrl, SentAt
            FROM dbo.ChatMessages WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        if (!Schema.Table("ChatMessages").Column("CorrelationId").Exists())
        {
            Alter.Table("ChatMessages")
                .AddColumn("CorrelationId").AsGuid().NotNullable().WithDefault(SystemMethods.NewSequentialId);
        }

        if (!Schema.Table("ChatMessages").Column("DeliveredAt").Exists())
        {
            Alter.Table("ChatMessages")
                .AddColumn("DeliveredAt").AsDateTime2().Nullable();
        }

        if (!Schema.Table("ChatMessages").Column("ReadAt").Exists())
        {
            Alter.Table("ChatMessages")
                .AddColumn("ReadAt").AsDateTime2().Nullable();
        }

        if (!Schema.Table("ChatMessages").Index("IX_ChatMessages_Correlation").Exists())
        {
            Create.Index("IX_ChatMessages_Correlation")
                .OnTable("ChatMessages")
                .OnColumn("CorrelationId").Ascending();
        }

        Execute.Sql(MessageAddWithReceipts);
    }

    public override void Down()
    {
        Execute.Sql(MessageAddWithoutReceipts);

        if (Schema.Table("ChatMessages").Index("IX_ChatMessages_Correlation").Exists())
            Delete.Index("IX_ChatMessages_Correlation").OnTable("ChatMessages");

        if (Schema.Table("ChatMessages").Column("ReadAt").Exists())
            Delete.Column("ReadAt").FromTable("ChatMessages");
        if (Schema.Table("ChatMessages").Column("DeliveredAt").Exists())
            Delete.Column("DeliveredAt").FromTable("ChatMessages");
        if (Schema.Table("ChatMessages").Column("CorrelationId").Exists())
            Delete.Column("CorrelationId").FromTable("ChatMessages");
    }
}
