using FluentMigrator;

namespace Sms.Migrations;

[Migration(129, "PeriodAttendanceRecords: indexes for advanced filtered queries")]
public sealed class M0129_PeriodAttendance_AdvancedQuery : Migration
{
    public override void Up()
    {
        Create.Index("IX_PeriodAttendance_Tenant_Date")
            .OnTable("PeriodAttendanceRecords")
            .OnColumn("TenantId").Ascending()
            .OnColumn("Date").Ascending();

        Create.Index("IX_PeriodAttendance_Tenant_Class_Date")
            .OnTable("PeriodAttendanceRecords")
            .OnColumn("TenantId").Ascending()
            .OnColumn("ClassId").Ascending()
            .OnColumn("Date").Ascending();

        Create.Index("IX_PeriodAttendance_Tenant_Subject")
            .OnTable("PeriodAttendanceRecords")
            .OnColumn("TenantId").Ascending()
            .OnColumn("Subject").Ascending();

        Create.Index("IX_PeriodAttendance_Tenant_MarkedBy")
            .OnTable("PeriodAttendanceRecords")
            .OnColumn("TenantId").Ascending()
            .OnColumn("MarkedBy").Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_PeriodAttendance_Tenant_MarkedBy").OnTable("PeriodAttendanceRecords");
        Delete.Index("IX_PeriodAttendance_Tenant_Subject").OnTable("PeriodAttendanceRecords");
        Delete.Index("IX_PeriodAttendance_Tenant_Class_Date").OnTable("PeriodAttendanceRecords");
        Delete.Index("IX_PeriodAttendance_Tenant_Date").OnTable("PeriodAttendanceRecords");
    }
}
