using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// M0148 and M0150 (the parent/student ChatThreads.Role/ChildId backfills) silently did
/// nothing: Users, Teachers, Staff, Students, ParentStudentLinks, and ChatThreads itself are
/// ALL under row-level-security tenant policies (rls.fn_tenant_predicate), which return zero
/// rows unless the session has IsPlatform=1 or a matching TenantId context — neither of which
/// a migration connection has by default. Every earlier backfill migration that touches these
/// tables (e.g. M0140's ParentStudentLinks backfill) explicitly sets IsPlatform=1 first; M0148
/// and M0150 forgot to, so their UPDATE ... FROM joins matched nothing and reported no error.
/// This re-runs the same backfill logic with the session context actually set.
/// </summary>
[Migration(151, "ChatThreads: re-run M0148/M0150's Role/ChildId backfill with RLS bypassed")]
public sealed class M0151_ChatThreads_RoleBackfillRlsFix : Migration
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
WHERE th.ContactUserId IS NULL AND th.IsGroup = 0;

UPDATE th
SET th.Role = N'Student'
FROM dbo.ChatThreads th
INNER JOIN dbo.Users u ON u.Id = th.ContactUserId
WHERE th.Role = N'Teacher'
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
