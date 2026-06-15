using FluentMigrator;

namespace Sms.Migrations;

[Migration(17, "Exams: Exams (term), ExamPapers, Grades with tenant RLS")]
public sealed class M0017_Exams_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Exams")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Type").AsString(40).Nullable()
            .WithColumn("Grades").AsString(40).Nullable()
            .WithColumn("FromDate").AsDate().Nullable()
            .WithColumn("ToDate").AsDate().Nullable()
            .WithColumn("SubjectCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("draft")
            .WithColumn("MarksEnteredPct").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Published").AsBoolean().NotNullable().WithDefaultValue(false);
        Create.Index("IX_Exams_Tenant").OnTable("Exams").OnColumn("TenantId").Ascending();

        Create.Table("ExamPapers")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ExamId").AsGuid().Nullable()
            .WithColumn("ClassId").AsGuid().Nullable()
            .WithColumn("Name").AsString(120).Nullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("SubjectId").AsGuid().Nullable()
            .WithColumn("Date").AsDate().Nullable()
            .WithColumn("StartTime").AsString(10).Nullable()
            .WithColumn("DurationMin").AsInt32().Nullable()
            .WithColumn("MaxMarks").AsInt32().NotNullable().WithDefaultValue(100)
            .WithColumn("Room").AsString(40).Nullable()
            .WithColumn("Invigilator1").AsString(120).Nullable()
            .WithColumn("Invigilator2").AsString(120).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("upcoming");
        Create.Index("IX_ExamPapers_Tenant_Exam").OnTable("ExamPapers")
            .OnColumn("TenantId").Ascending().OnColumn("ExamId").Ascending();

        Create.Table("Grades")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("StudentName").AsString(200).Nullable()
            .WithColumn("ExamPaperId").AsGuid().NotNullable()
            .WithColumn("Marks").AsDecimal(6, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("MaxMarks").AsDecimal(6, 2).NotNullable().WithDefaultValue(100)
            .WithColumn("Grade").AsString(4).Nullable()
            .WithColumn("Gpa").AsDecimal(4, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Pass").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Date").AsDate().Nullable();
        Create.UniqueConstraint("UQ_Grades_Paper_Student")
            .OnTable("Grades").Columns("TenantId", "ExamPaperId", "StudentId");

        foreach (var t in new[] { "Exams", "ExamPapers", "Grades" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "Grades", "ExamPapers", "Exams" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("Grades");
        Delete.Table("ExamPapers");
        Delete.Table("Exams");
    }
}
