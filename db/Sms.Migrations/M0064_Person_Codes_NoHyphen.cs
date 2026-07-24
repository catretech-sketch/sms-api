using FluentMigrator;

namespace Sms.Migrations;

[Migration(64, "Person codes without hyphens: {slug}STU{yy}0001")]
public sealed class M0064_Person_Codes_NoHyphen : Migration
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