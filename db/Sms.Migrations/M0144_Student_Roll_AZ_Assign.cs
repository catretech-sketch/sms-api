using FluentMigrator;

namespace Sms.Migrations;

[Migration(144, "Assign student Roll A–Z by name within class/section on create/update; backfill existing")]
public sealed class M0144_Student_Roll_AZ_Assign : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_RenumberClass"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisguardian.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisguardian.Student_Update"))
            Execute.Sql(sql);

        Execute.Sql("""
;WITH ranked AS (
    SELECT Id,
           ROW_NUMBER() OVER (
               PARTITION BY TenantId, ISNULL(Grade, N''), ISNULL(Section, N'')
               ORDER BY Name ASC, AdmissionNo ASC, Id ASC
           ) AS rn
    FROM dbo.Students
    WHERE Status = N'active'
)
UPDATE s SET Roll = r.rn
FROM dbo.Students s
INNER JOIN ranked r ON r.Id = s.Id;

UPDATE dbo.Students SET Roll = 0 WHERE Status <> N'active';
""");
    }

    public override void Down() { }
}
