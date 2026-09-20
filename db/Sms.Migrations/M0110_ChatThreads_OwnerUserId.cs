using FluentMigrator;

namespace Sms.Migrations;

[Migration(110, "ChatThreads: OwnerUserId for per-user inbox")]
public sealed class M0110_ChatThreads_OwnerUserId : Migration
{
    private const string ThreadCreateInline = """
        CREATE OR ALTER PROCEDURE dbo.Thread_Create
            @TenantId uniqueidentifier,
            @OwnerUserId uniqueidentifier,
            @Name nvarchar(120),
            @Role nvarchar(40),
            @IsGroup bit,
            @ChildId uniqueidentifier
        AS
        BEGIN
            SET NOCOUNT ON;

            DECLARE @Existing uniqueidentifier;
            SELECT TOP 1 @Existing = Id
            FROM dbo.ChatThreads
            WHERE TenantId = @TenantId
              AND OwnerUserId = @OwnerUserId
              AND Name = @Name;

            IF @Existing IS NOT NULL
            BEGIN
                SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId
                FROM dbo.ChatThreads
                WHERE Id = @Existing;
                RETURN;
            END

            DECLARE @Id uniqueidentifier = NEWID();
            INSERT dbo.ChatThreads (Id, TenantId, OwnerUserId, Name, Role, IsGroup, ChildId)
            VALUES (@Id, @TenantId, @OwnerUserId, @Name, @Role, ISNULL(@IsGroup, 0), @ChildId);

            SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId
            FROM dbo.ChatThreads WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        Alter.Table("ChatThreads")
            .AddColumn("OwnerUserId").AsGuid().Nullable();

        Create.Index("IX_ChatThreads_Owner")
            .OnTable("ChatThreads")
            .OnColumn("TenantId").Ascending()
            .OnColumn("OwnerUserId").Ascending()
            .OnColumn("Name").Ascending();

        // Apply OwnerUserId Thread_Create INLINE (embedded procs/comms stays at pre-OwnerUserId baseline).
        Execute.Sql(ThreadCreateInline);

        // Remove legacy school-wide shared threads (no owner) so each inbox is private.
        Execute.Sql(@"
DELETE m FROM dbo.ChatMessages m
INNER JOIN dbo.ChatThreads t ON t.Id = m.ThreadId
WHERE t.OwnerUserId IS NULL;
DELETE FROM dbo.ChatThreads WHERE OwnerUserId IS NULL;");
    }

    public override void Down()
    {
        Delete.Index("IX_ChatThreads_Owner").OnTable("ChatThreads");
        Delete.Column("OwnerUserId").FromTable("ChatThreads");
    }
}
