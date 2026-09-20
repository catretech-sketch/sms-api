using FluentMigrator;

namespace Sms.Migrations;

[Migration(111, "Chat: delete legacy shared threads with no owner")]
public sealed class M0111_ChatThreads_CleanupShared : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.ChatThreads', 'OwnerUserId') IS NOT NULL
BEGIN
    DELETE m FROM dbo.ChatMessages m
    INNER JOIN dbo.ChatThreads t ON t.Id = m.ThreadId
    WHERE t.OwnerUserId IS NULL;
    DELETE FROM dbo.ChatThreads WHERE OwnerUserId IS NULL;
END");
    }

    public override void Down()
    {
        // Data cleanup is not reversible.
    }
}
