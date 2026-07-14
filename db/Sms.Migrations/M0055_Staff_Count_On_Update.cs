using FluentMigrator;

namespace Sms.Migrations;

[Migration(55, "Recalculate Tenants.StaffCount on Teacher_Update / Staff_Update")]
public sealed class M0055_Staff_Count_On_Update : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Teacher_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
    }
}
