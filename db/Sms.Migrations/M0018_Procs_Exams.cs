using FluentMigrator;

namespace Sms.Migrations;

[Migration(18, "Exams procs: Exam_Create/Update, ExamPaper_Create, Grade_Upsert (auto grade/gpa/pass)")]
public sealed class M0018_Procs_Exams : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.exams."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var name in new[] { "Exam_Create", "Exam_Update", "ExamPaper_Create", "Grade_Upsert" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
    }
}
