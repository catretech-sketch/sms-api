using FluentMigrator;

namespace Sms.Migrations;

[Migration(136, "Teacher/Staff PATCH: persist Gender, Exp, EmployeeCode (create already wrote these)")]
public sealed class M0136_Staffing_Update_Gender_Exp_Code : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingpatch.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingpatch.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingidentity.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingemail.Staff_Update"))
            Execute.Sql(sql);
    }
}
