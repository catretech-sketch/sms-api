using FluentMigrator;

namespace Sms.Migrations;

[Migration(10, "Catre Phase 1 ops procs: Onboarding/Ticket/Team writes (embedded CREATE OR ALTER)")]
public sealed class M0010_Procs_Catre_Ops : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catreops."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[]
        {
            "Onboarding_Create", "Onboarding_Advance", "Onboarding_SetChecklist",
            "Ticket_Create", "Ticket_Update", "Ticket_AddMessage", "Team_Invite", "Team_Update"
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
