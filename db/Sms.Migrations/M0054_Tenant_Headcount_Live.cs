using FluentMigrator;

namespace Sms.Migrations;

[Migration(54, "Keep Tenants student/staff counts in sync; backfill from SIS tables")]
public sealed class M0054_Tenant_Headcount_Live : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Teacher_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Staff_Create"))
            Execute.Sql(sql);

        Execute.Sql(@"
UPDATE t
SET
    StudentsCount = ISNULL((
        SELECT COUNT(*) FROM dbo.Students s
        WHERE s.TenantId = t.Id AND s.Status = N'active'
    ), 0),
    StaffCount = ISNULL((
        SELECT COUNT(*) FROM dbo.Teachers te
        WHERE te.TenantId = t.Id AND te.Status = N'active'
    ), 0) + ISNULL((
        SELECT COUNT(*) FROM dbo.Staff st
        WHERE st.TenantId = t.Id AND st.Status = N'active'
    ), 0)
FROM dbo.Tenants t;
");
    }

    public override void Down()
    {
        // Backfill is not reversed; proc CREATE OR ALTER leaves latest definitions in place.
    }
}
