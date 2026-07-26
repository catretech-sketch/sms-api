using FluentMigrator;

namespace Sms.Migrations;

[Migration(103, "TimetableSlots.TeacherId: sms-admin assigns a teacher per slot (not per subject), so " +
                "the previous Subjects.TeacherId join could never reflect the real per-period assignment")]
public sealed class M0103_TimetableSlot_TeacherId : Migration
{
    public override void Up()
    {
        Alter.Table("TimetableSlots").AddColumn("TeacherId").AsGuid().Nullable();

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Create
    @TenantId uniqueidentifier, @Day nvarchar(3), @Period int, @Subject nvarchar(80) = NULL,
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL, @Room nvarchar(40) = NULL,
    @StartTime nvarchar(10) = NULL, @EndTime nvarchar(10) = NULL, @TeacherId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime, TeacherId)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Day, @Period, @Subject, @ClassId, @ClassName, @Room, @StartTime, @EndTime, @TeacherId);
    SELECT Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime
    FROM dbo.TimetableSlots WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Create
    @TenantId uniqueidentifier, @Day nvarchar(3), @Period int, @Subject nvarchar(80) = NULL,
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL, @Room nvarchar(40) = NULL,
    @StartTime nvarchar(10) = NULL, @EndTime nvarchar(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.TimetableSlots (TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Day, @Period, @Subject, @ClassId, @ClassName, @Room, @StartTime, @EndTime);
    SELECT Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime
    FROM dbo.TimetableSlots WHERE Id = (SELECT Id FROM @ins);
END;");
        Delete.Column("TeacherId").FromTable("TimetableSlots");
    }
}
