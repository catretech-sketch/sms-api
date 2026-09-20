using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// Every prior ContactUserId backfill (M0147/M0150) matched ChatThreads.Name EXACTLY against
/// Users/Teachers/Staff.Name. But the admin's "message this student's parent" contact entry
/// stores the thread name as "&lt;Student&gt; (parent)" (a display suffix appended client-side,
/// e.g. "Rahul Sharma (parent)") — that string never matches any real account name, so every
/// thread created that way stayed ContactUserId = NULL forever, and its messages were never
/// delivered to the parent's own inbox. This strips the " (parent)" suffix and matches the
/// student roster instead, only when exactly one student in the tenant has that name.
/// </summary>
[Migration(154, "ChatThreads: backfill ContactUserId for legacy '<Student> (parent)' threads")]
public sealed class M0154_ChatThreads_ParentSuffixBackfill : Migration
{
    public override void Up()
    {
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
