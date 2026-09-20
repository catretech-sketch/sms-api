using FluentMigrator;

namespace Sms.Migrations;

[Migration(102, "Exam attendance roll-call: ExamAttendanceRecords (+ TVP type + bulk upsert proc) with tenant RLS")]
public sealed class M0102_ExamAttendance_Tables : Migration
{
    public override void Up()
    {
        Create.Table("ExamAttendanceRecords")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ExamPaperId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("MarkedBy").AsGuid().Nullable();
        Create.UniqueConstraint("UQ_ExamAttendance_Paper_Student")
            .OnTable("ExamAttendanceRecords").Columns("TenantId", "ExamPaperId", "StudentId");

        Execute.Sql(@"CREATE TYPE dbo.ExamAttendanceTvp AS TABLE (StudentId uniqueidentifier, Status nvarchar(20));");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.ExamAttendance_BulkUpsert
    @TenantId uniqueidentifier, @ExamPaperId uniqueidentifier,
    @MarkedBy uniqueidentifier, @Rows dbo.ExamAttendanceTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.ExamAttendanceRecords AS tgt
    USING (SELECT StudentId, Status FROM @Rows) AS src
        ON tgt.TenantId = @TenantId AND tgt.ExamPaperId = @ExamPaperId AND tgt.StudentId = src.StudentId
    WHEN MATCHED THEN
        UPDATE SET Status = src.Status, MarkedBy = @MarkedBy
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, ExamPaperId, StudentId, Status, MarkedBy)
        VALUES (NEWID(), @TenantId, @ExamPaperId, src.StudentId, src.Status, @MarkedBy);
END;");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.ExamAttendanceRecordsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ExamAttendanceRecords,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ExamAttendanceRecords AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ExamAttendanceRecordsTenantPolicy;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.ExamAttendance_BulkUpsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.ExamAttendanceTvp;");
        Delete.Table("ExamAttendanceRecords");
    }
}
