using FluentMigrator;

namespace Sms.Migrations;

[Migration(89, "Announcement_Create: accept CreatorUserId (embedded CREATE OR ALTER)")]
public sealed class M0089_Announcement_Create_CreatorUserId : Migration
{
    public override void Up()
    {
        // Kept under procs/commsidentity (not procs/comms) so M0028's broad
        // "procs.comms." EmbeddedProcs fragment doesn't pick this body up and
        // re-create it referencing CreatorUserId ~60 migrations before M0088 adds
        // the column — the same ordering pitfall fixed for M0086/M0087.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.commsidentity.Announcement_Create"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: previous proc body is superseded, not restored.
    }
}
