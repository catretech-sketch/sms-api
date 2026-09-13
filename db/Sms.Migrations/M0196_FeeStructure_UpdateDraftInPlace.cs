using FluentMigrator;

namespace Sms.Migrations;

/// Editing a Draft and saving used to always insert yet another new row, even though nothing
/// had ever depended on that draft (it was never published, so no invoice or "what was live on
/// date X" record could reference it). That's real history-loss protection turning into
/// clutter. Now: saving with an @Id whose CURRENT status is not 'active' updates that row in
/// place (bumping CreatedAt to reflect the edit time) instead of creating a new one. Saving
/// with no @Id, or an @Id that's currently the published version, still always inserts a new
/// row — the live version is never mutated out from under whoever is relying on it; you save a
/// new draft and publish that instead.
[Migration(196, "Fees: editing a draft updates it in place instead of always inserting")]
public sealed class M0196_FeeStructure_UpdateDraftInPlace : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Upsert
    @TenantId uniqueidentifier,
    @Id uniqueidentifier = NULL,
    @Name nvarchar(200),
    @AcademicYear nvarchar(20),
    @ClassGrade nvarchar(40) = NULL,
    @Section nvarchar(20) = NULL,
    @Currency nvarchar(10),
    @EffectiveFrom date,
    @EffectiveTo date = NULL,
    @Status nvarchar(20),
    @Description nvarchar(max) = NULL,
    @AmountsJson nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingStatus nvarchar(20);
    IF @Id IS NOT NULL
        SELECT @ExistingStatus = Status FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;

    IF @ExistingStatus IS NOT NULL AND LOWER(@ExistingStatus) <> N'active'
    BEGIN
        -- Editing a draft (never the currently-published version) — update it in place.
        IF LOWER(@Status) = N'active'
            UPDATE dbo.FeeStructures SET Status = N'inactive'
            WHERE TenantId = @TenantId AND LOWER(Status) = N'active' AND Id <> @Id;

        UPDATE dbo.FeeStructures
        SET Name = @Name, AcademicYear = @AcademicYear, ClassGrade = @ClassGrade, Section = @Section,
            Currency = @Currency, EffectiveFrom = @EffectiveFrom, EffectiveTo = @EffectiveTo,
            Status = @Status, Description = @Description, AmountsJson = @AmountsJson,
            CreatedAt = SYSUTCDATETIME()
        WHERE Id = @Id AND TenantId = @TenantId;

        SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
               EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt
        FROM dbo.FeeStructures WHERE Id = @Id;
        RETURN;
    END

    -- No @Id, or @Id names the currently-published version: always insert a new, immutable
    -- version — the live one (or 'no specific draft') is never mutated in place.
    DECLARE @TargetId uniqueidentifier = NEWID();
    IF LOWER(@Status) = N'active'
        UPDATE dbo.FeeStructures SET Status = N'inactive' WHERE TenantId = @TenantId AND LOWER(Status) = N'active';

    INSERT dbo.FeeStructures (
        Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
        EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt)
    VALUES (
        @TargetId, @TenantId, @Name, @AcademicYear, @ClassGrade, @Section, @Currency,
        @EffectiveFrom, @EffectiveTo, @Status, @Description, @AmountsJson, SYSUTCDATETIME());

    SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
           EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt
    FROM dbo.FeeStructures WHERE Id = @TargetId;
END;");
    }

    public override void Down()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Upsert
    @TenantId uniqueidentifier,
    @Id uniqueidentifier = NULL,
    @Name nvarchar(200),
    @AcademicYear nvarchar(20),
    @ClassGrade nvarchar(40) = NULL,
    @Section nvarchar(20) = NULL,
    @Currency nvarchar(10),
    @EffectiveFrom date,
    @EffectiveTo date = NULL,
    @Status nvarchar(20),
    @Description nvarchar(max) = NULL,
    @AmountsJson nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TargetId uniqueidentifier = NEWID();

    IF LOWER(@Status) = N'active'
        UPDATE dbo.FeeStructures SET Status = N'inactive' WHERE TenantId = @TenantId AND LOWER(Status) = N'active';

    INSERT dbo.FeeStructures (
        Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
        EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt)
    VALUES (
        @TargetId, @TenantId, @Name, @AcademicYear, @ClassGrade, @Section, @Currency,
        @EffectiveFrom, @EffectiveTo, @Status, @Description, @AmountsJson, SYSUTCDATETIME());

    SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
           EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt
    FROM dbo.FeeStructures WHERE Id = @TargetId;
END;");
    }
}
