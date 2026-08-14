using FluentMigrator;

namespace Sms.Migrations;

[Migration(119, "Student_EnsureLogin: fetch roster student and create login if missing (embedded CREATE OR ALTER)")]
public sealed class M0119_Student_EnsureLogin : Migration
{
    public override void Up()
    {
        // New folder (not procs/identity) so earlier identity EmbeddedProcs fragments
        // cannot pick these up and overwrite them.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Student_EnsureLogin"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.User_ListByAdmissionId"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Student_GetByAdmissionNo"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Student_EnsureLogin;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_ListByAdmissionId;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Student_GetByAdmissionNo;");
    }
}
