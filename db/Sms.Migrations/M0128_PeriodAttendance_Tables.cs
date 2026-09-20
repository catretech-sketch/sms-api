using FluentMigrator;

namespace Sms.Migrations;

[Migration(128, "PeriodAttendanceRecords: subject+period marks (+ TVP + bulk upsert) with tenant RLS")]
public sealed class M0128_PeriodAttendance_Tables : Migration
{
    public override void Up()
    {
        Create.Table("PeriodAttendanceRecords")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("ClassId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Period").AsInt32().NotNullable()
            .WithColumn("PeriodId").AsGuid().Nullable() // TimetableSlots.Id when known
            .WithColumn("Subject").AsString(120).NotNullable()
            .WithColumn("SubjectId").AsGuid().Nullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("MarkedBy").AsGuid().Nullable()
            .WithColumn("MarkedByRole").AsString(64).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.UniqueConstraint("UQ_PeriodAttendance_Class_Student_Date_Period_Subject")
            .OnTable("PeriodAttendanceRecords")
            .Columns("TenantId", "ClassId", "StudentId", "Date", "Period", "Subject");

        Create.Index("IX_PeriodAttendance_Student_Date")
            .OnTable("PeriodAttendanceRecords")
            .OnColumn("TenantId").Ascending()
            .OnColumn("StudentId").Ascending()
            .OnColumn("Date").Ascending();

        Execute.Sql(@"CREATE TYPE dbo.PeriodAttendanceTvp AS TABLE (
    StudentId uniqueidentifier NOT NULL,
    Status nvarchar(20) NOT NULL
);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PeriodAttendance_BulkUpsert
    @TenantId uniqueidentifier,
    @ClassId uniqueidentifier,
    @Date date,
    @Period int,
    @PeriodId uniqueidentifier = NULL,
    @Subject nvarchar(120),
    @SubjectId uniqueidentifier = NULL,
    @MarkedBy uniqueidentifier = NULL,
    @MarkedByRole nvarchar(64) = NULL,
    @Rows dbo.PeriodAttendanceTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now datetime2 = SYSUTCDATETIME();
    MERGE dbo.PeriodAttendanceRecords AS tgt
    USING (SELECT StudentId, Status FROM @Rows) AS src
        ON tgt.TenantId = @TenantId
       AND tgt.ClassId = @ClassId
       AND tgt.StudentId = src.StudentId
       AND tgt.[Date] = @Date
       AND tgt.Period = @Period
       AND tgt.Subject = @Subject
    WHEN MATCHED THEN
        UPDATE SET
            Status = src.Status,
            PeriodId = COALESCE(@PeriodId, tgt.PeriodId),
            SubjectId = COALESCE(@SubjectId, tgt.SubjectId),
            MarkedBy = @MarkedBy,
            MarkedByRole = @MarkedByRole,
            UpdatedAt = @Now
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, ClassId, StudentId, [Date], Period, PeriodId, Subject, SubjectId, Status, MarkedBy, MarkedByRole, CreatedAt, UpdatedAt)
        VALUES (NEWID(), @TenantId, @ClassId, src.StudentId, @Date, @Period, @PeriodId, @Subject, @SubjectId, src.Status, @MarkedBy, @MarkedByRole, @Now, @Now);
END;");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.PeriodAttendanceRecordsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PeriodAttendanceRecords,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PeriodAttendanceRecords AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.PeriodAttendanceRecordsTenantPolicy;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PeriodAttendance_BulkUpsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.PeriodAttendanceTvp;");
        Delete.Table("PeriodAttendanceRecords");
    }
}
