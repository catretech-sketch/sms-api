using FluentMigrator;

namespace Sms.Migrations;

[Migration(100, "TimetableSlot_Delete proc, for admin timetable publish reconciliation")]
public sealed class M0100_TimetableSlot_Delete : Migration
{
    public override void Up()
    {
        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Delete
    @Id uniqueidentifier, @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.TimetableSlots WHERE Id = @Id AND TenantId = @TenantId;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TimetableSlot_Delete;");
    }
}
