using FluentMigrator;

namespace Sms.Migrations;

/// Version 141: local Sms already applied 140 as LeaveRequests.Note, so ParentStudentLinks
/// must not reuse 140 or MigrateUp will skip the table.
[Migration(141, "ParentStudentLinks: multi-child parent roster with tenant RLS and backfill")]
public sealed class M0141_ParentStudentLinks : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF OBJECT_ID(N'dbo.ParentStudentLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParentStudentLinks (
        ParentUserId uniqueidentifier NOT NULL,
        StudentId    uniqueidentifier NOT NULL,
        TenantId     uniqueidentifier NOT NULL,
        CreatedAt    datetime2 NOT NULL CONSTRAINT DF_ParentStudentLinks_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ParentStudentLinks PRIMARY KEY (ParentUserId, StudentId)
    );
    CREATE INDEX IX_ParentStudentLinks_Tenant_Student
        ON dbo.ParentStudentLinks (TenantId, StudentId);
END
""");

        Execute.Sql("""
IF NOT EXISTS (
    SELECT 1 FROM sys.security_policies WHERE name = N'ParentStudentLinksTenantPolicy'
)
BEGIN
    EXEC(N'
CREATE SECURITY POLICY rls.ParentStudentLinksTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ParentStudentLinks,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ParentStudentLinks AFTER INSERT
WITH (STATE = ON)');
END
""");

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
  AND LTRIM(RTRIM(u.StudentId)) <> N''
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
        Execute.Sql("DROP TABLE IF EXISTS dbo.ParentStudentLinks;");
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
    }
}
