using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// ResolveSenderRoleLabelAsync (CommsModule.cs) used to default EVERY sender that wasn't a
/// Teacher/Staff row to the hardcoded label "Teacher" — so every parent-inbox-delivery thread
/// (a parent messaging a teacher first) got mislabeled "Teacher" in the recipient's inbox, and
/// never carried the ChildId that would let a teacher tell same-named parents' threads apart.
/// That code path is now fixed for NEW threads (checks ParentStudentLinks before falling back),
/// but Role/ChildId are stored columns, not computed live — so existing rows created before the
/// fix stay wrong until backfilled here.
/// </summary>
[Migration(148, "ChatThreads: backfill parent-thread Role + ChildId mislabeled by the old default")]
public sealed class M0148_ChatThreads_ParentRoleBackfill : Migration
{
    public override void Up()
    {
        // Fix Role: any thread whose ContactUserId is a linked parent (in ParentStudentLinks)
        // and NOT a Teacher/Staff account, but was stored as "Teacher".
        Execute.Sql("""
UPDATE th
SET th.Role = N'Parent'
FROM dbo.ChatThreads th
WHERE th.Role = N'Teacher'
  AND th.ContactUserId IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM dbo.ParentStudentLinks pl
      WHERE pl.ParentUserId = th.ContactUserId AND pl.TenantId = th.TenantId
  )
  AND NOT EXISTS (SELECT 1 FROM dbo.Teachers t WHERE t.UserId = th.ContactUserId AND t.TenantId = th.TenantId)
  AND NOT EXISTS (SELECT 1 FROM dbo.Staff s WHERE s.UserId = th.ContactUserId AND s.TenantId = th.TenantId);
""");

        // Backfill ChildId on those same mirrored threads from the parent's own thread with
        // this contact (OwnerUserId/ContactUserId swapped), so the teacher's inbox shows which
        // child the conversation is about.
        Execute.Sql("""
UPDATE th
SET th.ChildId = src.ChildId
FROM dbo.ChatThreads th
CROSS APPLY (
    SELECT TOP 1 p.ChildId
    FROM dbo.ChatThreads p
    WHERE p.TenantId = th.TenantId
      AND p.OwnerUserId = th.ContactUserId
      AND p.ContactUserId = th.OwnerUserId
      AND p.ChildId IS NOT NULL
    ORDER BY p.LastAt DESC
) src
WHERE th.ChildId IS NULL
  AND th.ContactUserId IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM dbo.ParentStudentLinks pl
      WHERE pl.ParentUserId = th.ContactUserId AND pl.TenantId = th.TenantId
  );
""");
    }

    public override void Down()
    {
        // Not reversible — the original mislabeled values aren't recoverable, and leaving the
        // corrected data in place on rollback is strictly safer than re-breaking it.
    }
}
