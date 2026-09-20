using FluentMigrator;

namespace Sms.Migrations;

[Migration(40, "Timetable: TimetableSlots table + tenant RLS + insert proc")]
public sealed class M0040_Timetable_Tables : Migration
{
    public override void Up()
    {
        Create.Table("TimetableSlots")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Day").AsString(3).NotNullable()
            .WithColumn("Period").AsInt32().NotNullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("ClassId").AsGuid().Nullable()
            .WithColumn("ClassName").AsString(80).Nullable()
            .WithColumn("Room").AsString(40).Nullable()
            .WithColumn("StartTime").AsString(10).Nullable()
            .WithColumn("EndTime").AsString(10).Nullable();
        Create.Index("IX_TimetableSlots_Tenant").OnTable("TimetableSlots").OnColumn("TenantId").Ascending();

        Execute.Sql(@"CREATE SECURITY POLICY rls.TimetableSlotsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TimetableSlots,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TimetableSlots AFTER INSERT
WITH (STATE = ON);");

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
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TimetableSlot_Create;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TimetableSlotsTenantPolicy;");
        Delete.Table("TimetableSlots");
    }
}
