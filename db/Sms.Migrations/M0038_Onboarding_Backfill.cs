using FluentMigrator;

namespace Sms.Migrations;

[Migration(38, "Wire clients into the onboarding pipeline: re-apply Onboarding_Create (TenantId + seeded checklist) and backfill existing tenants")]
public sealed class M0038_Onboarding_Backfill : Migration
{
    public override void Up()
    {
        // Re-apply the catre-ops procs so the updated Onboarding_Create (now persists TenantId
        // and starts 'Account created' done) is in place on already-migrated databases.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catreops."))
            Execute.Sql(sql);

        // Backfill an onboarding card for every trial/active tenant that doesn't have one yet.
        // Idempotent: re-running skips tenants that already have a linked card.
        Execute.Sql(@"
INSERT dbo.OnboardingItems (Id, TenantId, Name, Slug, Owner, Value, Stage, Age)
SELECT NEWID(), t.Id, t.Name, t.Slug, t.Csm, t.Mrr,
       CASE t.Status WHEN 'active' THEN 'active' ELSE 'trial' END,
       DATEDIFF(day, t.CreatedAt, SYSUTCDATETIME())
FROM dbo.Tenants t
WHERE t.PlanId IS NOT NULL
  AND t.Status IN ('trial', 'active')
  AND NOT EXISTS (SELECT 1 FROM dbo.OnboardingItems o WHERE o.TenantId = t.Id);");

        // Seed the standard 5-item checklist for any tenant-linked card that has none.
        Execute.Sql(@"
INSERT dbo.OnboardingChecklist (OnboardingId, Seq, Label, Done)
SELECT o.Id, v.Seq, v.Label, v.Done
FROM dbo.OnboardingItems o
CROSS APPLY (VALUES
    (1, 'Account created', CONVERT(bit, 1)),
    (2, 'Admin invited',   CONVERT(bit, 0)),
    (3, 'Data imported',   CONVERT(bit, 0)),
    (4, 'First login',     CONVERT(bit, 0)),
    (5, 'Payment set up',  CONVERT(bit, 0))) v(Seq, Label, Done)
WHERE o.TenantId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.OnboardingChecklist c WHERE c.OnboardingId = o.Id);");
    }

    public override void Down()
    {
        // Remove only the tenant-linked (backfilled / auto-created) cards and their checklists.
        Execute.Sql(@"
DELETE c FROM dbo.OnboardingChecklist c
JOIN dbo.OnboardingItems o ON o.Id = c.OnboardingId
WHERE o.TenantId IS NOT NULL;");
        Execute.Sql("DELETE FROM dbo.OnboardingItems WHERE TenantId IS NOT NULL;");
    }
}
