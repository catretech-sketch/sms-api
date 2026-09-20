using FluentMigrator;

namespace Sms.Migrations;

[Migration(70, "Attendance: per-tenant AttendanceAlertConfigs (absence-alert thresholds + daily schedule) with RLS + Get/Upsert procs")]
public sealed class M0070_Attendance_Alert_Config : Migration
{
    public override void Up()
    {
        Create.Table("AttendanceAlertConfigs")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("NoticeDays").AsInt32().NotNullable().WithDefaultValue(3)
            .WithColumn("EmailDays").AsInt32().NotNullable().WithDefaultValue(5)
            .WithColumn("AutoSend").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("AutoTime").AsString(5).NotNullable().WithDefaultValue("09:00")
            .WithColumn("AutoChannel").AsString(10).NotNullable().WithDefaultValue("app")
            .WithColumn("LastAutoSentDate").AsDate().Nullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("UX_AttendanceAlertConfigs_Tenant").OnTable("AttendanceAlertConfigs")
            .OnColumn("TenantId").Ascending().WithOptions().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.AttendanceAlertConfigsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AttendanceAlertConfigs,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AttendanceAlertConfigs AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AttendanceAlertConfig_Get
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT NoticeDays, EmailDays, AutoSend, AutoTime, AutoChannel, LastAutoSentDate
    FROM dbo.AttendanceAlertConfigs
    WHERE TenantId = @TenantId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AttendanceAlertConfig_Upsert
    @TenantId uniqueidentifier,
    @NoticeDays int,
    @EmailDays int,
    @AutoSend bit,
    @AutoTime nvarchar(5),
    @AutoChannel nvarchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AttendanceAlertConfigs
       SET NoticeDays = @NoticeDays,
           EmailDays = @EmailDays,
           AutoSend = @AutoSend,
           AutoTime = @AutoTime,
           AutoChannel = @AutoChannel,
           UpdatedAt = SYSUTCDATETIME()
     WHERE TenantId = @TenantId;

    IF @@ROWCOUNT = 0
        INSERT dbo.AttendanceAlertConfigs (TenantId, NoticeDays, EmailDays, AutoSend, AutoTime, AutoChannel)
        VALUES (@TenantId, @NoticeDays, @EmailDays, @AutoSend, @AutoTime, @AutoChannel);

    SELECT NoticeDays, EmailDays, AutoSend, AutoTime, AutoChannel, LastAutoSentDate
    FROM dbo.AttendanceAlertConfigs
    WHERE TenantId = @TenantId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.AttendanceAlertConfig_Upsert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.AttendanceAlertConfig_Get;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AttendanceAlertConfigsTenantPolicy;");
        Delete.Table("AttendanceAlertConfigs");
    }
}
