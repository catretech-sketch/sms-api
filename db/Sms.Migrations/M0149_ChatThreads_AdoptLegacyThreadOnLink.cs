using FluentMigrator;

namespace Sms.Migrations;

/// A thread created before M0147 (ContactUserId) has ContactUserId = NULL, matched only by
/// Name. Once that same contact gets a real link — either the CRM explicitly starts a new
/// chat with them, or they simply reply for the first time and delivery resolves a
/// ContactUserId — Thread_Create's ContactUserId-based dedupe can't find the old row (it has
/// no ContactUserId to match), so it created a brand-new thread instead of continuing the
/// existing conversation. That's how the same contact ended up with two separate threads.
/// Thread_Create now adopts that legacy same-Name row (stamping its ContactUserId) instead of
/// duplicating it, for both the CRM-create path and the reply-delivery mirror path.
[Migration(149, "Thread_Create: adopt a legacy NULL-ContactUserId thread instead of duplicating it")]
public sealed class M0149_ChatThreads_AdoptLegacyThreadOnLink : Migration
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

                IF @Existing IS NULL
                BEGIN
                    -- Adopt a legacy same-name thread from before ContactUserId existed,
                    -- rather than creating a second conversation with this same contact.
                    SELECT TOP 1 @Existing = Id
                    FROM dbo.ChatThreads
                    WHERE TenantId = @TenantId
                      AND OwnerUserId = @OwnerUserId
                      AND ContactUserId IS NULL
                      AND IsGroup = ISNULL(@IsGroup, 0)
                      AND Name = @Name;

                    IF @Existing IS NOT NULL
                        UPDATE dbo.ChatThreads
                        SET ContactUserId = @ContactUserId, Role = @Role
                        WHERE Id = @Existing;
                END
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

    public override void Up() => Execute.Sql(ThreadCreateInline);

    public override void Down() { }
}
