using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// Second pass of M0151's backfill: this is live data, and new threads kept getting created
/// between M0151 running and this migration being written (e.g. "Ankit Rana" ended up with
/// three separate ChatThreads rows, only one of which existed — and got backfilled — when
/// M0151 ran). Re-running the same idempotent backfill catches anything created since. Rows
/// already correct are simply not matched by the WHERE clauses, so this is safe to run again.
/// </summary>
[Migration(152, "ChatThreads: re-sweep Role/ChildId backfill for threads created since M0151")]
public sealed class M0152_ChatThreads_RoleBackfillSweep : Migration
{
    public override void Up()
    {
        Execute.Sql("""
EXEC sp_set_session_context @key=N'IsPlatform', @value=1;

UPDATE th
SET th.ContactUserId = x.Id
FROM dbo.ChatThreads th
CROSS APPLY (
    SELECT TOP 1 c.Id
    FROM (
        SELECT u.Id, 1 AS Pri FROM dbo.Users u
        WHERE u.TenantId = th.TenantId AND u.Name = th.Name AND u.Id <> th.OwnerUserId
        UNION ALL
        SELECT t.UserId, 2 AS Pri FROM dbo.Teachers t
        WHERE t.TenantId = th.TenantId AND t.Name = th.Name AND t.UserId IS NOT NULL AND t.UserId <> th.OwnerUserId
        UNION ALL
        SELECT s.UserId, 3 AS Pri FROM dbo.Staff s
        WHERE s.TenantId = th.TenantId AND s.Name = th.Name AND s.UserId IS NOT NULL AND s.UserId <> th.OwnerUserId
    ) c
    WHERE c.Id IS NOT NULL
    ORDER BY c.Pri
) x
WHERE th.ContactUserId IS NULL AND th.IsGroup = 0 AND th.OwnerUserId IS NOT NULL;

UPDATE th
SET th.Role = N'Student'
FROM dbo.ChatThreads th
INNER JOIN dbo.Users u ON u.Id = th.ContactUserId
WHERE th.Role <> N'Student'
  AND NULLIF(LTRIM(RTRIM(u.StudentId)), '') IS NOT NULL;

UPDATE th
SET th.Role = N'Parent'
FROM dbo.ChatThreads th
WHERE th.Role = N'Teacher'
  AND th.ContactUserId IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM dbo.ParentStudentLinks pl
      WHERE pl.ParentUserId = th.ContactUserId AND pl.TenantId = th.TenantId
  );

UPDATE th
SET th.ChildId = y.ChildId
FROM dbo.ChatThreads th
CROSS APPLY (
    SELECT TOP 1 ChildId FROM (
        SELECT s.Id AS ChildId, 1 AS Pri
        FROM dbo.Users u
        INNER JOIN dbo.Students s
            ON s.TenantId = th.TenantId AND LOWER(LTRIM(RTRIM(s.AdmissionNo))) = LOWER(LTRIM(RTRIM(u.StudentId)))
        WHERE u.Id = th.ContactUserId AND NULLIF(LTRIM(RTRIM(u.StudentId)), '') IS NOT NULL
        UNION ALL
        SELECT pl.StudentId AS ChildId, 2 AS Pri
        FROM dbo.ParentStudentLinks pl
        WHERE pl.ParentUserId = th.ContactUserId AND pl.TenantId = th.TenantId
    ) z ORDER BY Pri
) y
WHERE th.ChildId IS NULL AND th.Role IN (N'Student', N'Parent') AND th.ContactUserId IS NOT NULL;
""");
    }

    public override void Down()
    {
        // Not reversible — the original mislabeled values aren't recoverable, and leaving the
        // corrected data in place on rollback is strictly safer than re-breaking it.
    }
}
