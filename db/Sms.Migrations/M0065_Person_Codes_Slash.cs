using FluentMigrator;

namespace Sms.Migrations;

[Migration(65, "Person codes with slash format: {slug}/STU/{yy}/{####}")]
public sealed class M0065_Person_Codes_Slash : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sis.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Teacher_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing.Staff_Create"))
            Execute.Sql(sql);
    }

    public override void Down() { }
}