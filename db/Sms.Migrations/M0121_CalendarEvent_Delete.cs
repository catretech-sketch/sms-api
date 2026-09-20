using FluentMigrator;

namespace Sms.Migrations;

[Migration(121, "Calendar: delete proc + ChannelsJson column for CRM calendar parity")]
public sealed class M0121_CalendarEvent_Delete : Migration
{
    public override void Up()
    {
        Alter.Table("CalendarEvents")
            .AddColumn("ChannelsJson").AsString(200).Nullable();

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.CalendarEvent_Create
    @TenantId uniqueidentifier,
    @Title nvarchar(200),
    @Date date,
    @Time nvarchar(10) = NULL,
    @Type nvarchar(20),
    @Description nvarchar(max) = NULL,
    @ChannelsJson nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.CalendarEvents (TenantId, Title, [Date], Time, Type, Description, ChannelsJson)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Title, @Date, @Time, @Type, @Description, @ChannelsJson);
    SELECT Id, TenantId, Title, [Date], Time, Type, Description, ChannelsJson
    FROM dbo.CalendarEvents WHERE Id = (SELECT Id FROM @ins);
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.CalendarEvent_Delete
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.CalendarEvents WHERE Id = @Id AND TenantId = @TenantId;
    SELECT @@ROWCOUNT AS Deleted;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.CalendarEvent_Delete;");
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.CalendarEvent_Create
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
        Delete.Column("ChannelsJson").FromTable("CalendarEvents");
    }
}
