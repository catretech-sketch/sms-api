using FluentMigrator;

namespace Sms.Migrations;

[Migration(115, "Re-apply Teacher_Update/Staff_Update final SELECT (EmployeeCode) — fixes PATCH /v1/teachers|staff 500")]
public sealed class M0115_Staffing_Update_Procs_Fix : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingidentity.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingemail.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // Procs are superseded, not restored.
    }
}
