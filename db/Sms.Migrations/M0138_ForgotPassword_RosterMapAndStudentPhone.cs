using FluentMigrator;

namespace Sms.Migrations;

[Migration(138, "Forgot-password: map GuardianEmail on roster read; Student_EnsureLogin skips colliding guardian phone")]
public sealed class M0138_ForgotPassword_RosterMapAndStudentPhone : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Student_EnsureLogin"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Student_GetByAdmissionNo"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Student_EnsureLogin"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Student_GetByAdmissionNo"))
            Execute.Sql(sql);
    }
}
