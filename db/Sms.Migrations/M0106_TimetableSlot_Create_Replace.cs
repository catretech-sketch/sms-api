using FluentMigrator;

namespace Sms.Migrations;

[Migration(106, "TimetableSlot_Create: replace (not accumulate) a class's existing slot at the " +
                "same day/period; add a unique index so re-publishing can't silently duplicate rows")]
public sealed class M0106_TimetableSlot_Create_Replace : Migration
{
    public override void Up()
    {
        // Collapse any duplicates TimetableSlot_Create already let through (no uniqueness
        // was ever enforced), keeping one row per class/day/period: prefer a row with a
        // TeacherId set (a duplicate created before a teacher was assigned shouldn't win
        // over one where it was), then the most recently inserted. Elevate this connection
        // to platform first — the tenant RLS filter predicate would otherwise hide every
        // other tenant's rows from this DELETE (it doesn't set a TenantId session context),
        // silently doing nothing and leaving CREATE UNIQUE INDEX below to fail on the
        // still-present duplicates. Same pattern as M0077's cross-tenant backfill.
        Execute.Sql(@"
EXEC sp_set_session_context @key=N'IsPlatform', @value=1;
WITH ranked AS (
    SELECT Id,
           ROW_NUMBER() OVER (
               PARTITION BY TenantId, ClassId, [Day], Period
               ORDER BY CASE WHEN TeacherId IS NOT NULL THEN 0 ELSE 1 END, Id DESC
           ) AS rn
    FROM dbo.TimetableSlots
    WHERE ClassId IS NOT NULL
)
DELETE ts FROM dbo.TimetableSlots ts
JOIN ranked r ON r.Id = ts.Id
WHERE r.rn > 1;");

        // Filtered (ClassId can be null for slots not yet tied to a class) so those don't
        // collide with each other under the unique index.
        Execute.Sql(@"
CREATE UNIQUE INDEX UX_TimetableSlots_Class_Day_Period
ON dbo.TimetableSlots (TenantId, ClassId, [Day], Period)
WHERE ClassId IS NOT NULL;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.TimetableSlot_Create
    @TenantId uniqueidentifier, @Day nvarchar(3), @Period int, @Subject nvarchar(80) = NULL,
    @ClassId uniqueidentifier = NULL, @ClassName nvarchar(80) = NULL, @Room nvarchar(40) = NULL,
    @StartTime nvarchar(10) = NULL, @EndTime nvarchar(10) = NULL, @TeacherId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Re-publishing the same class/day/period (e.g. editing a slot, or re-running a
    -- timetable import) replaces the prior slot instead of accumulating a duplicate row
    -- alongside it — duplicates made teacher-name/subject lookups pick an arbitrary one.
    IF @ClassId IS NOT NULL
        DELETE dbo.TimetableSlots
        WHERE TenantId = @TenantId AND ClassId = @ClassId AND [Day] = @Day AND Period = @Period;

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
        Execute.Sql("DROP INDEX IF EXISTS UX_TimetableSlots_Class_Day_Period ON dbo.TimetableSlots;");

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
}
