using FluentMigrator;

namespace Sms.Migrations;

[Migration(95, "Teacher_Update/Staff_Update: return EmployeeCode (pre-existing gap since M0063 added the column)")]
public sealed class M0095_Teacher_Staff_Update_EmployeeCode : Migration
{
    public override void Up()
    {
        // Pre-existing bug, not introduced by this migration: Teacher_Update/Staff_Update's
        // final SELECT never returned EmployeeCode after M0063 added the column, even though
        // Teacher_Create/Staff_Create (also M0063) do. TeacherRow/StaffResponse expect 19/14
        // columns respectively, so PATCH /v1/teachers/{id} and /v1/staff/{id} 500 on Dapper's
        // "no matching constructor" every time.
        //
        // Kept under procs/staffingidentity (not procs/staffing) so M0014's broad
        // "procs.staffing." EmbeddedProcs fragment doesn't pick these bodies up and
        // re-create them referencing EmployeeCode ~50 migrations before M0063 adds the
        // column - the same ordering pitfall fixed for M0086/M0087/M0089/M0091/M0093.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingidentity.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingidentity.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: previous proc bodies are superseded, not restored.
    }
}
