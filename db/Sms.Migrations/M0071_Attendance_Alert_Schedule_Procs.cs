using FluentMigrator;

namespace Sms.Migrations;

[Migration(71, "Attendance: scheduled auto-send procs (ListDue across tenants + MarkAutoSent) for the absence-alert worker")]
public sealed class M0071_Attendance_Alert_Schedule_Procs : Migration
{
    public override void Up()
    {
        // Tenants whose daily auto-send is enabled, whose scheduled wall-clock time has passed
        // for "today" (local time computed by the worker), and that have not been swept yet today.
        // Called on a platform session (IsPlatform = 1) so RLS returns every tenant's row.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AttendanceAlertConfig_ListDue
    @Today date,
    @NowMinutes int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TenantId, NoticeDays, EmailDays, AutoChannel
    FROM dbo.AttendanceAlertConfigs
    WHERE AutoSend = 1
      AND (LastAutoSentDate IS NULL OR LastAutoSentDate < @Today)
      AND (DATEPART(HOUR, CAST(AutoTime AS time)) * 60 + DATEPART(MINUTE, CAST(AutoTime AS time))) <= @NowMinutes;
END");

        // Records that today's sweep ran for a tenant so it is not sent again the same day.
        // Called on that tenant's session context.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AttendanceAlertConfig_MarkAutoSent
    @TenantId uniqueidentifier,
    @Date date
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AttendanceAlertConfigs
       SET LastAutoSentDate = @Date,
           UpdatedAt = SYSUTCDATETIME()
     WHERE TenantId = @TenantId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.AttendanceAlertConfig_MarkAutoSent;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.AttendanceAlertConfig_ListDue;");
    }
}
