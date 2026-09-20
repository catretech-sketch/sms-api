using FluentMigrator;

namespace Sms.Migrations;

/// TeacherResponse/TeacherRow gained UserId (M0180 follow-up, for the transport traveling-
/// teacher/bus-duty pickers which need a teacher's linked login account, not their Teachers.Id).
/// dbo.Teacher_Create's SELECT must return it too, or Dapper's TeacherRow binding breaks on
/// create. Kept in its own namespace fragment (not procs/staffing) so M0014's broad
/// "procs.staffing." EmbeddedProcs replay doesn't re-create this body ~70 migrations before
/// M0084 adds Teachers.UserId — the same ordering pitfall M0095/M0136 already worked around.
[Migration(181, "Teacher_Create: return UserId so admin pickers can resolve a teacher's linked login")]
public sealed class M0181_TeacherCreate_Return_UserId : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingteacheruserid.Teacher_Create"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc body (without UserId) is superseded, not restored.
    }
}
