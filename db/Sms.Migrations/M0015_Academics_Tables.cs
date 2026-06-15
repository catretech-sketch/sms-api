using FluentMigrator;

namespace Sms.Migrations;

[Migration(15, "Academics: Classes, Subjects, AttendanceRecords (+ TVP type) with tenant RLS")]
public sealed class M0015_Academics_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Classes")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable()
            .WithColumn("Grade").AsString(20).Nullable()
            .WithColumn("Section").AsString(20).Nullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("Room").AsString(40).Nullable()
            .WithColumn("StudentCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ClassTeacherId").AsGuid().Nullable();
        Create.Index("IX_Classes_Tenant").OnTable("Classes").OnColumn("TenantId").Ascending();

        Create.Table("Subjects")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable()
            .WithColumn("Short").AsString(20).Nullable()
            .WithColumn("TeacherId").AsGuid().Nullable()
            .WithColumn("Color").AsString(40).Nullable();
        Create.Index("IX_Subjects_Tenant").OnTable("Subjects").OnColumn("TenantId").Ascending();

        Create.Table("AttendanceRecords")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ClassId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("MarkedBy").AsGuid().Nullable();
        Create.UniqueConstraint("UQ_Attendance_Class_Student_Date")
            .OnTable("AttendanceRecords").Columns("TenantId", "ClassId", "StudentId", "Date");

        // Table-valued parameter type for bulk roll-call upserts (one round-trip, set-based).
        Execute.Sql(@"CREATE TYPE dbo.AttendanceTvp AS TABLE (StudentId uniqueidentifier, Status nvarchar(20));");

        foreach (var t in new[] { "Classes", "Subjects", "AttendanceRecords" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AttendanceRecordsTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.SubjectsTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ClassesTenantPolicy;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.AttendanceTvp;");
        Delete.Table("AttendanceRecords");
        Delete.Table("Subjects");
        Delete.Table("Classes");
    }
}
