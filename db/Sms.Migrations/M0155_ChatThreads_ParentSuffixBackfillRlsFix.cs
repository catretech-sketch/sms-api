using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// M0154's backfill ran without bypassing RLS (unlike M0141/M0151, which do), so its
/// filter-predicate-gated cross-tenant-looking joins (ChatThreads/Students/ParentStudentLinks
/// all carry tenant RLS) silently matched zero rows instead of erroring — the UPDATE was a
/// no-op. Re-running the identical backfill here with IsPlatform set, same fix as M0151.
/// </summary>
[Migration(155, "ChatThreads: re-run M0154's parent-suffix backfill with RLS bypassed")]
public sealed class M0155_ChatThreads_ParentSuffixBackfillRlsFix : Migration
{
    public override void Up()
    {
        Execute.Sql("EXEC sp_set_session_context @key=N'IsPlatform', @value=1;");

        Execute.Sql("""
UPDATE th
SET th.ContactUserId = x.ParentUserId,
    th.Role = N'Parent'
FROM dbo.ChatThreads th
CROSS APPLY (
    SELECT TOP 1 pl.ParentUserId
    FROM dbo.Students st
    INNER JOIN dbo.ParentStudentLinks pl ON pl.StudentId = st.Id AND pl.TenantId = st.TenantId
    WHERE st.TenantId = th.TenantId
      AND st.Name = LTRIM(RTRIM(LEFT(th.Name, LEN(th.Name) - 9)))
      AND pl.ParentUserId <> th.OwnerUserId
      AND (
          SELECT COUNT(1) FROM dbo.Students st2
          WHERE st2.TenantId = th.TenantId
            AND st2.Name = LTRIM(RTRIM(LEFT(th.Name, LEN(th.Name) - 9)))
      ) = 1
) x
WHERE th.ContactUserId IS NULL
  AND th.IsGroup = 0
  AND th.Name LIKE N'% (parent)';
""");
    }

    public override void Down()
    {
        // Not reversible — the original mislabeled state isn't worth restoring.
    }
}
