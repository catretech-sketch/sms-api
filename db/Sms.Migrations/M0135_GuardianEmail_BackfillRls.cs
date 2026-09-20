using FluentMigrator;

namespace Sms.Migrations;

[Migration(135, "Backfill Students.GuardianEmail from extras with RLS bypass (M0134 update saw 0 rows)")]
public sealed class M0135_GuardianEmail_BackfillRls : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);

        // M0134 copied extras→GuardianEmail without IsPlatform, so RLS hid every row.
        Execute.Sql("""
EXEC sp_set_session_context @key=N'IsPlatform', @value=1;

UPDATE s
SET GuardianEmail = LEFT(LTRIM(RTRIM(COALESCE(
    NULLIF(JSON_VALUE(pe.ExtrasJson, '$.father.email'), N''),
    NULLIF(JSON_VALUE(pe.ExtrasJson, '$.mother.email'), N'')
))), 256)
FROM dbo.Students s
INNER JOIN dbo.PersonExtras pe
    ON pe.PersonId = s.Id AND pe.PersonType = N'student' AND pe.TenantId = s.TenantId
WHERE s.GuardianEmail IS NULL
  AND (
        NULLIF(LTRIM(RTRIM(JSON_VALUE(pe.ExtrasJson, '$.father.email'))), N'') IS NOT NULL
     OR NULLIF(LTRIM(RTRIM(JSON_VALUE(pe.ExtrasJson, '$.mother.email'))), N'') IS NOT NULL
  );
""");
    }

    public override void Down()
    {
        // Data backfill — keep GuardianEmail values.
    }
}
