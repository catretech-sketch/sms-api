using FluentMigrator;

namespace Sms.Migrations;

/// ChatThreads.Name/Role were free-text, with no reliable link to the actual Users row a
/// thread is "about" — delivery into the other party's own inbox depended on matching
/// ChatThreads.Name against Users/Teachers/Staff.Name, which silently failed whenever the
/// name didn't match exactly or the contact had no linked login. ContactUserId is a real
/// FK so CRM-created threads (and their delivery) can target an actual account.
[Migration(147, "ChatThreads: ContactUserId — real recipient link, replacing name matching")]
public sealed class M0147_ChatThreads_ContactUserId : Migration
{
    private const string ThreadCreateInline = """
        CREATE OR ALTER PROCEDURE dbo.Thread_Create
            @TenantId uniqueidentifier,
            @OwnerUserId uniqueidentifier,
            @Name nvarchar(120),
            @Role nvarchar(40),
            @IsGroup bit,
            @ChildId uniqueidentifier,
            @ContactUserId uniqueidentifier = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            DECLARE @Existing uniqueidentifier;

            IF @ContactUserId IS NOT NULL
            BEGIN
                SELECT TOP 1 @Existing = Id
                FROM dbo.ChatThreads
                WHERE TenantId = @TenantId
                  AND OwnerUserId = @OwnerUserId
                  AND ContactUserId = @ContactUserId;
            END
            ELSE
            BEGIN
                SELECT TOP 1 @Existing = Id
                FROM dbo.ChatThreads
                WHERE TenantId = @TenantId
                  AND OwnerUserId = @OwnerUserId
                  AND ContactUserId IS NULL
                  AND Name = @Name;
            END

            IF @Existing IS NOT NULL
            BEGIN
                SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId
                FROM dbo.ChatThreads
                WHERE Id = @Existing;
                RETURN;
            END

            DECLARE @Id uniqueidentifier = NEWID();
            INSERT dbo.ChatThreads (Id, TenantId, OwnerUserId, Name, Role, IsGroup, ChildId, ContactUserId)
            VALUES (@Id, @TenantId, @OwnerUserId, @Name, @Role, ISNULL(@IsGroup, 0), @ChildId, @ContactUserId);

            SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId
            FROM dbo.ChatThreads WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        Alter.Table("ChatThreads")
            .AddColumn("ContactUserId").AsGuid().Nullable();

        Create.Index("IX_ChatThreads_Contact")
            .OnTable("ChatThreads")
            .OnColumn("TenantId").Ascending()
            .OnColumn("OwnerUserId").Ascending()
            .OnColumn("ContactUserId").Ascending();

        Execute.Sql(ThreadCreateInline);

        // Best-effort backfill for existing threads: link ContactUserId wherever the old
        // Name-matching would already have resolved one, so old conversations benefit too.
        Execute.Sql("""
            UPDATE th
            SET th.ContactUserId = x.Id
            FROM dbo.ChatThreads th
            CROSS APPLY (
                SELECT TOP 1 c.Id
                FROM (
                    SELECT u.Id, 1 AS Pri FROM dbo.Users u
                    WHERE u.TenantId = th.TenantId AND u.Name = th.Name AND u.Id <> th.OwnerUserId
                    UNION ALL
                    SELECT t.UserId, 2 AS Pri FROM dbo.Teachers t
                    WHERE t.TenantId = th.TenantId AND t.Name = th.Name AND t.UserId IS NOT NULL AND t.UserId <> th.OwnerUserId
                    UNION ALL
                    SELECT s.UserId, 3 AS Pri FROM dbo.Staff s
                    WHERE s.TenantId = th.TenantId AND s.Name = th.Name AND s.UserId IS NOT NULL AND s.UserId <> th.OwnerUserId
                ) c
                WHERE c.Id IS NOT NULL
                ORDER BY c.Pri
            ) x
            WHERE th.ContactUserId IS NULL AND th.IsGroup = 0;
            """);
    }

    public override void Down()
    {
        Delete.Index("IX_ChatThreads_Contact").OnTable("ChatThreads");
        Delete.Column("ContactUserId").FromTable("ChatThreads");
    }
}
