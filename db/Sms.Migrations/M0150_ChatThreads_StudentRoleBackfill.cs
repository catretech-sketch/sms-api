using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// M0148 fixed Role for parent threads that already had ContactUserId set — but two gaps
/// remained, found via a real mislabeled contact ("Rahul Sharma", a STUDENT login, shown as
/// "Teacher"): (1) ResolveSenderRoleLabelAsync never checked for a student sender at all
/// (students get their own Users.StudentId-linked login here, not only a parent's — see
/// CommsModule.cs), and (2) threads created between M0147's one-time ContactUserId backfill
/// and now can still have ContactUserId IS NULL, so M0148's join-on-ContactUserId fix never
/// reached them. This re-runs the ContactUserId name-match backfill, then fixes Role/ChildId
/// for both parent AND student contacts.
/// </summary>
[Migration(150, "ChatThreads: backfill ContactUserId + Role/ChildId for student contacts too")]
public sealed class M0150_ChatThreads_StudentRoleBackfill : Migration
{
    public override void Up()
    {
        // Re-run: some threads were created after M0147's one-off backfill and never got
        // ContactUserId linked, so Role/ChildId correction below can't find them.
        Execute.Sql("""
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
""");

        Execute.Sql("""
UPDATE th
SET th.Role = N'Student'
FROM dbo.ChatThreads th
INNER JOIN dbo.Users u ON u.Id = th.ContactUserId
WHERE th.Role = N'Teacher'
  AND NULLIF(LTRIM(RTRIM(u.StudentId)), '') IS NOT NULL;
""");

        Execute.Sql("""
UPDATE th
SET th.Role = N'Parent'
FROM dbo.ChatThreads th
WHERE th.Role = N'Teacher'
  AND th.ContactUserId IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM dbo.ParentStudentLinks pl
      WHERE pl.ParentUserId = th.ContactUserId AND pl.TenantId = th.TenantId
  );
""");

        Execute.Sql("""
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
