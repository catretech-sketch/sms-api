using FluentMigrator;

namespace Sms.Migrations;

[Migration(118, "Student_Update: accept Gender/Dob/Email/Address — PATCH /students/{id} previously dropped these")]
public sealed class M0118_Students_Update_Gender_Dob_Email_Address : Migration
{
    public override void Up()
    {
        // New folder (not procs/sis or procs/sisphoto) so no earlier migration's
        // EmbeddedProcs("procs.sis.Student_Update"/"procs.sisphoto.Student_Update")
        // re-embeds and overwrites this body — same ordering pitfall noted in
        // M0099_Students_PhotoUrl.cs.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisemail.Student_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisphoto.Student_Update"))
            Execute.Sql(sql);
    }
}
