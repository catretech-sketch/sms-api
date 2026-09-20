using FluentMigrator;

namespace Sms.Migrations;

[Migration(143, "Production blockers: procs that persist payment-invoice, avatar_hue, timetable teacher_id, period geo")]
public sealed class M0143_ProductionBlockerProcs : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FeePayments_Invoice' AND object_id = OBJECT_ID(N'dbo.FeePayments'))
    CREATE INDEX IX_FeePayments_Invoice ON dbo.FeePayments (TenantId, InvoiceId) WHERE InvoiceId IS NOT NULL;
""");

        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.FeePayment_Create
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ClassLabel nvarchar(40), @FeeType nvarchar(20), @Amount decimal(18,2), @Method nvarchar(40), @Ref nvarchar(80),
    @InvoiceId uniqueidentifier = NULL, @HeadId nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId)
    VALUES (@Id, @TenantId, @StudentId, @StudentName, @ClassLabel, ISNULL(@FeeType, 'academic'),
        ISNULL(@Amount, 0), @Method, @Ref, CAST(SYSUTCDATETIME() AS date), @InvoiceId, @HeadId);

    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
    FROM dbo.FeePayments WHERE Id = @Id;
END
""");

        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.Student_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Grade nvarchar(20), @Section nvarchar(20), @Roll int,
    @GuardianName nvarchar(200), @GuardianPhone nvarchar(40), @GuardianEmail nvarchar(256), @House nvarchar(40),
    @FeeStatus nvarchar(20), @FeeDue decimal(18,2), @Status nvarchar(20), @PhotoUrl nvarchar(max) = NULL,
    @SetPhoto bit = 0, @Gender nvarchar(1) = NULL, @Dob datetime2 = NULL, @Email nvarchar(256) = NULL,
    @Address nvarchar(500) = NULL, @AvatarHue int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Students SET
        Name = ISNULL(@Name, Name),
        Grade = ISNULL(@Grade, Grade),
        Section = ISNULL(@Section, Section),
        ClassLabel = ISNULL(@Grade, Grade) + '-' + ISNULL(@Section, Section),
        Roll = ISNULL(@Roll, Roll),
        GuardianName = ISNULL(@GuardianName, GuardianName),
        GuardianPhone = ISNULL(@GuardianPhone, GuardianPhone),
        GuardianEmail = ISNULL(@GuardianEmail, GuardianEmail),
        House = ISNULL(@House, House),
        FeeStatus = ISNULL(@FeeStatus, FeeStatus),
        FeeDue = ISNULL(@FeeDue, FeeDue),
        Status = ISNULL(@Status, Status),
        PhotoUrl = CASE WHEN @SetPhoto = 1 THEN @PhotoUrl ELSE PhotoUrl END,
        Gender = ISNULL(@Gender, Gender),
        Dob = ISNULL(@Dob, Dob),
        Email = ISNULL(@Email, Email),
        Address = ISNULL(@Address, Address),
        AvatarHue = ISNULL(@AvatarHue, AvatarHue)
    WHERE Id = @Id;

    DECLARE @TenantId uniqueidentifier =
        (SELECT TOP 1 TenantId FROM dbo.Students WHERE Id = @Id);
    IF @TenantId IS NOT NULL
        UPDATE dbo.Tenants
        SET StudentsCount = (
            SELECT COUNT(*) FROM dbo.Students s WHERE s.TenantId = @TenantId AND s.Status = N'active'
        )
        WHERE Id = @TenantId;

    SELECT Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName,
           GuardianPhone, GuardianEmail, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address,
           PhotoUrl
    FROM dbo.Students WHERE Id = @Id;
END
""");

        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Create
    @TenantId uniqueidentifier, @Day nvarchar(3), @Period int, @Subject nvarchar(80) = NULL,
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL, @Room nvarchar(40) = NULL,
    @StartTime nvarchar(10) = NULL, @EndTime nvarchar(10) = NULL, @TeacherId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ClassId IS NOT NULL
        DELETE dbo.TimetableSlots
        WHERE TenantId = @TenantId AND ClassId = @ClassId AND [Day] = @Day AND Period = @Period;

    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime, TeacherId)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Day, @Period, @Subject, @ClassId, @ClassName, @Room, @StartTime, @EndTime, @TeacherId);
    SELECT Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime, TeacherId
    FROM dbo.TimetableSlots WHERE Id = (SELECT Id FROM @ins);
END
""");

        Execute.Sql("""
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
    @GeoFenceStatus nvarchar(32) = NULL,
    @GeoDistanceMeters int = NULL,
    @GeoCapturedAt datetime2 = NULL,
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
            UpdatedAt = @Now,
            GeoFenceStatus = COALESCE(@GeoFenceStatus, tgt.GeoFenceStatus),
            GeoDistanceMeters = COALESCE(@GeoDistanceMeters, tgt.GeoDistanceMeters),
            GeoCapturedAt = COALESCE(@GeoCapturedAt, tgt.GeoCapturedAt)
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, ClassId, StudentId, [Date], Period, PeriodId, Subject, SubjectId, Status, MarkedBy, MarkedByRole, UpdatedBy, UpdatedByRole, CreatedAt, UpdatedAt, GeoFenceStatus, GeoDistanceMeters, GeoCapturedAt)
        VALUES (NEWID(), @TenantId, @ClassId, src.StudentId, @Date, @Period, @PeriodId, @Subject, @SubjectId, src.Status, @MarkedBy, @MarkedByRole, @MarkedBy, @MarkedByRole, @Now, @Now, @GeoFenceStatus, @GeoDistanceMeters, @GeoCapturedAt)
    OUTPUT
        $action, inserted.Id, inserted.StudentId, deleted.Status, inserted.Status
        INTO @Changes (Action, RecordId, StudentId, FromStatus, ToStatus);

    INSERT INTO dbo.PeriodAttendanceAudit
        (Id, TenantId, RecordId, ClassId, StudentId, [Date], Period, Subject, FromStatus, ToStatus, ActorId, ActorName, ActorRole, At)
    SELECT NEWID(), @TenantId, c.RecordId, @ClassId, c.StudentId, @Date, @Period, @Subject, c.FromStatus, c.ToStatus, @MarkedBy, @ActorName, @MarkedByRole, @Now
    FROM @Changes c
    WHERE c.Action = 'INSERT' OR (c.Action = 'UPDATE' AND (c.FromStatus IS NULL OR c.FromStatus <> c.ToStatus));
END
""");
    }

    public override void Down()
    {
    }
}
