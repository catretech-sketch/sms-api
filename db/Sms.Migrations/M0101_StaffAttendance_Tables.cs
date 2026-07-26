using FluentMigrator;

namespace Sms.Migrations;

[Migration(101, "Teacher/staff roll-call attendance: StaffAttendanceRecords (+ TVP type + bulk upsert proc) with tenant RLS")]
public sealed class M0101_StaffAttendance_Tables : Migration
{
    public override void Up()
    {
        Create.Table("StaffAttendanceRecords")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("PersonType").AsString(10).NotNullable()
            .WithColumn("PersonId").AsGuid().NotNullable()
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("MarkedBy").AsGuid().Nullable();
        Create.UniqueConstraint("UQ_StaffAttendance_Type_Person_Date")
            .OnTable("StaffAttendanceRecords").Columns("TenantId", "PersonType", "PersonId", "Date");

        Execute.Sql(@"CREATE TYPE dbo.StaffAttendanceTvp AS TABLE (PersonId uniqueidentifier, Status nvarchar(20));");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.StaffAttendance_BulkUpsert
    @TenantId uniqueidentifier, @PersonType nvarchar(10), @Date date,
    @MarkedBy uniqueidentifier, @Rows dbo.StaffAttendanceTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.StaffAttendanceRecords AS tgt
    USING (SELECT PersonId, Status FROM @Rows) AS src
        ON tgt.TenantId = @TenantId AND tgt.PersonType = @PersonType
           AND tgt.PersonId = src.PersonId AND tgt.[Date] = @Date
    WHEN MATCHED THEN
        UPDATE SET Status = src.Status, MarkedBy = @MarkedBy
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, PersonType, PersonId, [Date], Status, MarkedBy)
        VALUES (NEWID(), @TenantId, @PersonType, src.PersonId, @Date, src.Status, @MarkedBy);
END;");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.StaffAttendanceRecordsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StaffAttendanceRecords,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StaffAttendanceRecords AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.StaffAttendanceRecordsTenantPolicy;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StaffAttendance_BulkUpsert;");
        Execute.Sql("DROP TYPE IF EXISTS dbo.StaffAttendanceTvp;");
        Delete.Table("StaffAttendanceRecords");
    }
}
