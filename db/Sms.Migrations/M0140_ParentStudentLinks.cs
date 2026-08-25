using FluentMigrator;

namespace Sms.Migrations;

[Migration(140, "ParentStudentLinks: multi-child parent roster with tenant RLS and backfill")]
public sealed class M0140_ParentStudentLinks : Migration
{
    public override void Up()
    {
        Create.Table("ParentStudentLinks")
            .WithColumn("ParentUserId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.PrimaryKey("PK_ParentStudentLinks").OnTable("ParentStudentLinks")
            .Columns("ParentUserId", "StudentId");
        Create.Index("IX_ParentStudentLinks_Tenant_Student").OnTable("ParentStudentLinks")
            .OnColumn("TenantId").Ascending().OnColumn("StudentId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.ParentStudentLinksTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ParentStudentLinks,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ParentStudentLinks AFTER INSERT
WITH (STATE = ON);");

        // Bypass RLS so historical Users/Students rows are visible during backfill.
        Execute.Sql("""
EXEC sp_set_session_context @key=N'IsPlatform', @value=1;

INSERT INTO dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId, CreatedAt)
SELECT u.Id, s.Id, s.TenantId, u.CreatedAt
FROM dbo.Users u
INNER JOIN dbo.UserRoles ur ON ur.UserId = u.Id AND ur.Role LIKE N'%parent%'
INNER JOIN dbo.Students s
    ON s.TenantId = u.TenantId
   AND LOWER(LTRIM(RTRIM(s.AdmissionNo))) = LOWER(LTRIM(RTRIM(u.StudentId)))
WHERE u.StudentId IS NOT NULL
  AND NOT EXISTS (
        SELECT 1 FROM dbo.ParentStudentLinks l
        WHERE l.ParentUserId = u.Id AND l.StudentId = s.Id);

INSERT INTO dbo.ParentStudentLinks (ParentUserId, StudentId, TenantId, CreatedAt)
SELECT u.Id, s.Id, s.TenantId, u.CreatedAt
FROM dbo.Users u
INNER JOIN dbo.UserRoles ur ON ur.UserId = u.Id AND ur.Role LIKE N'%parent%'
INNER JOIN dbo.Students s
    ON s.TenantId = u.TenantId
   AND u.Email IS NOT NULL
   AND s.GuardianEmail IS NOT NULL
   AND LOWER(LTRIM(RTRIM(s.GuardianEmail))) = LOWER(LTRIM(RTRIM(u.Email)))
WHERE NOT EXISTS (
        SELECT 1 FROM dbo.ParentStudentLinks l
        WHERE l.ParentUserId = u.Id AND l.StudentId = s.Id);
""");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ParentStudentLinksTenantPolicy;");
        Delete.Table("ParentStudentLinks");
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
    }
}
