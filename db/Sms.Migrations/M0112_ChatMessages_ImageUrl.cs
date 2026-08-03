using FluentMigrator;

namespace Sms.Migrations;

[Migration(112, "ChatMessages: ImageUrl + deliverable Message_Add proc")]
public sealed class M0112_ChatMessages_ImageUrl : Migration
{
    private const string MessageAddWithImageUrl = """
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
        if (!Schema.Table("ChatMessages").Column("ImageUrl").Exists())
        {
            Alter.Table("ChatMessages")
                .AddColumn("ImageUrl").AsString(int.MaxValue).Nullable();
        }

        Execute.Sql(MessageAddWithImageUrl);
    }

    public override void Down()
    {
        if (Schema.Table("ChatMessages").Column("ImageUrl").Exists())
            Delete.Column("ImageUrl").FromTable("ChatMessages");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.comms.Message_Add"))
            Execute.Sql(sql);
    }
}
