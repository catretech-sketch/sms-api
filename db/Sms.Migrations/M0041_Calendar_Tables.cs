using FluentMigrator;

namespace Sms.Migrations;

[Migration(41, "Calendar: CalendarEvents table + tenant RLS + insert proc")]
public sealed class M0041_Calendar_Tables : Migration
{
    public override void Up()
    {
        Create.Table("CalendarEvents")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Time").AsString(10).Nullable()
            .WithColumn("Type").AsString(20).NotNullable()
            .WithColumn("Description").AsString(int.MaxValue).Nullable();
        Create.Index("IX_CalendarEvents_Tenant").OnTable("CalendarEvents").OnColumn("TenantId").Ascending();

        Execute.Sql(@"CREATE SECURITY POLICY rls.CalendarEventsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.CalendarEvents,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.CalendarEvents AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.CalendarEvent_Create
    @TenantId uniqueidentifier, @Title nvarchar(200), @Date date, @Time nvarchar(10) = NULL,
    @Type nvarchar(20), @Description nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.CalendarEvents (TenantId, Title, [Date], Time, Type, Description)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Title, @Date, @Time, @Type, @Description);
    SELECT Id, TenantId, Title, [Date], Time, Type, Description
    FROM dbo.CalendarEvents WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.CalendarEvent_Create;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.CalendarEventsTenantPolicy;");
        Delete.Table("CalendarEvents");
    }
}
