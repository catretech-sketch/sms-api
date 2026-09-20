using FluentMigrator;

namespace Sms.Migrations;

[Migration(63, "EmployeeCode on Teachers/Staff; auto {slug}-STU/TCH/STF codes on create")]
public sealed class M0063_Person_Codes : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Teachers', 'EmployeeCode') IS NULL
    ALTER TABLE dbo.Teachers ADD EmployeeCode nvarchar(64) NULL;
IF COL_LENGTH('dbo.Staff', 'EmployeeCode') IS NULL
    ALTER TABLE dbo.Staff ADD EmployeeCode nvarchar(64) NULL;
");

        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Teachers_Tenant_EmployeeCode' AND object_id = OBJECT_ID(N'dbo.Teachers'))
    CREATE UNIQUE INDEX UX_Teachers_Tenant_EmployeeCode
        ON dbo.Teachers (TenantId, EmployeeCode)
        WHERE EmployeeCode IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Staff_Tenant_EmployeeCode' AND object_id = OBJECT_ID(N'dbo.Staff'))
    CREATE UNIQUE INDEX UX_Staff_Tenant_EmployeeCode
        ON dbo.Staff (TenantId, EmployeeCode)
        WHERE EmployeeCode IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Students_Tenant_AdmissionNo' AND object_id = OBJECT_ID(N'dbo.Students'))
   AND NOT EXISTS (
        SELECT 1 FROM dbo.Students
        GROUP BY TenantId, AdmissionNo
        HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_Students_Tenant_AdmissionNo
        ON dbo.Students (TenantId, AdmissionNo);
");

        Execute.Sql(@"
;WITH te AS (
    SELECT t.Id,
           LOWER(REPLACE(ISNULL(NULLIF(tn.Slug, N''), N'sch'), N'-', N'')) AS Slug,
           ROW_NUMBER() OVER (PARTITION BY t.TenantId ORDER BY t.CreatedAt, t.Id) AS n
    FROM dbo.Teachers t
    INNER JOIN dbo.Tenants tn ON tn.Id = t.TenantId
    WHERE t.EmployeeCode IS NULL OR LTRIM(RTRIM(t.EmployeeCode)) = N''
)
UPDATE t SET EmployeeCode = te.Slug + N'TCH' + RIGHT(CAST(YEAR(SYSUTCDATETIME()) AS nvarchar(4)), 2) + RIGHT(N'0000' + CAST(te.n AS nvarchar(10)), 4)
FROM dbo.Teachers t
INNER JOIN te ON te.Id = t.Id;

;WITH st AS (
    SELECT s.Id,
           LOWER(REPLACE(ISNULL(NULLIF(tn.Slug, N''), N'sch'), N'-', N'')) AS Slug,
           ROW_NUMBER() OVER (PARTITION BY s.TenantId ORDER BY s.CreatedAt, s.Id) AS n
    FROM dbo.Staff s
    INNER JOIN dbo.Tenants tn ON tn.Id = s.TenantId
    WHERE s.EmployeeCode IS NULL OR LTRIM(RTRIM(s.EmployeeCode)) = N''
)
UPDATE s SET EmployeeCode = st.Slug + N'STF' + RIGHT(CAST(YEAR(SYSUTCDATETIME()) AS nvarchar(4)), 2) + RIGHT(N'0000' + CAST(st.n AS nvarchar(10)), 4)
FROM dbo.Staff s
INNER JOIN st ON st.Id = s.Id;
");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Teacher_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Staff_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Students_Tenant_AdmissionNo' AND object_id = OBJECT_ID(N'dbo.Students'))
    DROP INDEX UX_Students_Tenant_AdmissionNo ON dbo.Students;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Teachers_Tenant_EmployeeCode' AND object_id = OBJECT_ID(N'dbo.Teachers'))
    DROP INDEX UX_Teachers_Tenant_EmployeeCode ON dbo.Teachers;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Staff_Tenant_EmployeeCode' AND object_id = OBJECT_ID(N'dbo.Staff'))
    DROP INDEX UX_Staff_Tenant_EmployeeCode ON dbo.Staff;
IF COL_LENGTH('dbo.Teachers', 'EmployeeCode') IS NOT NULL
    ALTER TABLE dbo.Teachers DROP COLUMN EmployeeCode;
IF COL_LENGTH('dbo.Staff', 'EmployeeCode') IS NOT NULL
    ALTER TABLE dbo.Staff DROP COLUMN EmployeeCode;
");
    }
}

