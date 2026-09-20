using FluentMigrator;

namespace Sms.Migrations;

[Migration(66, "Auto-assign student Roll by name A–Z within class/section")]
public sealed class M0066_Student_Roll_AZ : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Update"))
            Execute.Sql(sql);

        /* One-time re-number all active students A–Z per class. */
        Execute.Sql(@"
;WITH ranked AS (
    SELECT Id,
           ROW_NUMBER() OVER (
               PARTITION BY TenantId, Grade, Section
               ORDER BY Name ASC, AdmissionNo ASC, Id ASC
           ) AS rn
    FROM dbo.Students
    WHERE Status = N'active'
      AND Grade IS NOT NULL
      AND Section IS NOT NULL
)
UPDATE s SET Roll = r.rn
FROM dbo.Students s
INNER JOIN ranked r ON r.Id = s.Id;
");
    }

    public override void Down() { }
}