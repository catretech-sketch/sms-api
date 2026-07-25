using FluentMigrator;

namespace Sms.Migrations;

[Migration(99, "Students.PhotoUrl, teacher/admin-settable via PATCH /students/{id}")]
public sealed class M0099_Students_PhotoUrl : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Students', 'PhotoUrl') IS NULL
    ALTER TABLE dbo.Students ADD PhotoUrl nvarchar(max) NULL;
");

        // Kept under procs/sisphoto (not procs/sis) so M0054's and M0066's
        // "procs.sis.Student_Update" EmbeddedProcs re-embeds don't pick up this
        // body and re-create it referencing PhotoUrl before this migration's own
        // ALTER TABLE above has run — the same ordering pitfall already fixed for
        // M0086/M0095/M0097 in this repo (see M0097_Users_PhotoUrl.cs for the
        // identical situation on dbo.Users).
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisphoto.Student_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisphoto.Student_Create"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Students', 'PhotoUrl') IS NOT NULL
    ALTER TABLE dbo.Students DROP COLUMN PhotoUrl;
");
    }
}
