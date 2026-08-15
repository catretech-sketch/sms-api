using FluentMigrator;

namespace Sms.Migrations;

[Migration(139, "PeriodAttendanceRecords: geo-fence + UpdatedBy columns; PeriodAttendanceAudit history table")]
public sealed class M0139_PeriodAttendance_Geo_Audit : Migration
{
    public override void Up()
    {
        Alter.Table("PeriodAttendanceRecords")
            .AddColumn("GeoFenceStatus").AsString(32).Nullable()
            .AddColumn("GeoDistanceMeters").AsInt32().Nullable()
            .AddColumn("GeoCapturedAt").AsDateTime2().Nullable()
            .AddColumn("UpdatedBy").AsGuid().Nullable()
            .AddColumn("UpdatedByRole").AsString(64).Nullable();

        Create.Table("PeriodAttendanceAudit")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RecordId").AsGuid().NotNullable()
            .WithColumn("ClassId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Period").AsInt32().NotNullable()
            .WithColumn("Subject").AsString(120).NotNullable()
            .WithColumn("FromStatus").AsString(20).Nullable()
            .WithColumn("ToStatus").AsString(20).NotNullable()
            .WithColumn("ActorId").AsGuid().Nullable()
            .WithColumn("ActorName").AsString(200).Nullable()
            .WithColumn("ActorRole").AsString(64).Nullable()
            .WithColumn("At").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_PeriodAttendanceAudit_Record")
            .OnTable("PeriodAttendanceAudit")
            .OnColumn("TenantId").Ascending()
            .OnColumn("RecordId").Ascending()
            .OnColumn("At").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.PeriodAttendanceAuditTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PeriodAttendanceAudit,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PeriodAttendanceAudit AFTER INSERT
WITH (STATE = ON);");

        // Re-create the bulk-upsert proc: stamp UpdatedBy/UpdatedByRole on every
        // write and append an audit row per record whose status actually changed
        // (skip idempotent re-saves with an unchanged status).
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
    DECLARE @ActorName nvarchar(200) = (SELECT Name FROM dbo.Users WHERE Id = @MarkedBy);

    DECLARE @Changes TABLE (
        Action nvarchar(10) NOT NULL,
        RecordId uniqueidentifier NOT NULL,
        StudentId uniqueidentifier NOT NULL,
        FromStatus nvarchar(20) NULL,
        ToStatus nvarchar(20) NOT NULL
    );

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
            UpdatedBy = @MarkedBy,
            UpdatedByRole = @MarkedByRole,
            UpdatedAt = @Now
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, ClassId, StudentId, [Date], Period, PeriodId, Subject, SubjectId, Status, MarkedBy, MarkedByRole, UpdatedBy, UpdatedByRole, CreatedAt, UpdatedAt)
        VALUES (NEWID(), @TenantId, @ClassId, src.StudentId, @Date, @Period, @PeriodId, @Subject, @SubjectId, src.Status, @MarkedBy, @MarkedByRole, @MarkedBy, @MarkedByRole, @Now, @Now)
    OUTPUT
        $action, inserted.Id, inserted.StudentId, deleted.Status, inserted.Status
        INTO @Changes (Action, RecordId, StudentId, FromStatus, ToStatus);

    INSERT INTO dbo.PeriodAttendanceAudit
        (Id, TenantId, RecordId, ClassId, StudentId, [Date], Period, Subject, FromStatus, ToStatus, ActorId, ActorName, ActorRole, At)
    SELECT NEWID(), @TenantId, c.RecordId, @ClassId, c.StudentId, @Date, @Period, @Subject, c.FromStatus, c.ToStatus, @MarkedBy, @ActorName, @MarkedByRole, @Now
    FROM @Changes c
    WHERE c.Action = 'INSERT' OR (c.Action = 'UPDATE' AND (c.FromStatus IS NULL OR c.FromStatus <> c.ToStatus));
END;");
    }

    public override void Down()
    {
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

        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.PeriodAttendanceAuditTenantPolicy;");
        Delete.Table("PeriodAttendanceAudit");

        Delete.Column("UpdatedByRole").FromTable("PeriodAttendanceRecords");
        Delete.Column("UpdatedBy").FromTable("PeriodAttendanceRecords");
        Delete.Column("GeoCapturedAt").FromTable("PeriodAttendanceRecords");
        Delete.Column("GeoDistanceMeters").FromTable("PeriodAttendanceRecords");
        Delete.Column("GeoFenceStatus").FromTable("PeriodAttendanceRecords");
    }
}
