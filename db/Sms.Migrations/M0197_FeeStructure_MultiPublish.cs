using FluentMigrator;

namespace Sms.Migrations;

/// Multiple fee structures can now be Published (Status = 'active') at the same time.
/// Previously, saving or publishing one version with Status = 'active' always retired
/// whichever version(s) were already active back to 'inactive' — a "single live row" rule.
/// That meant publishing a second, independent fee (e.g. Transport, then Exam) silently
/// un-published the first one. There is no longer any such rule: publishing/unpublishing one
/// version never touches any other version's status. Invoice generation now merges every
/// currently-Published version's amounts (FeeService.GenerateInvoicesAsync /
/// GetStructureAsync), so no fee head is lost — and if the same (class, head) pair is set by
/// more than one Published version, the most recently created one wins so it is never
/// double-counted.
[Migration(197, "Fees: Publish/Unpublish no longer retire other structures; add explicit Unpublish")]
public sealed class M0197_FeeStructure_MultiPublish : Migration
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
        -- Publishing/unpublishing here never touches any other version's Status.
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
    -- version — the live one (or 'no specific draft') is never mutated in place. No other
    -- version's Status is ever touched by this insert.
    DECLARE @TargetId uniqueidentifier = NEWID();

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

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Publish
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        SELECT CAST(0 AS bit) AS Found, CAST(NULL AS uniqueidentifier) AS Id;
        RETURN;
    END

    -- Publishing this version never retires any other version — many can be Published at once.
    UPDATE dbo.FeeStructures SET Status = N'active' WHERE Id = @Id AND TenantId = @TenantId;

    SELECT CAST(1 AS bit) AS Found, Id
    FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Unpublish
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        SELECT CAST(0 AS bit) AS Found, CAST(NULL AS uniqueidentifier) AS Id;
        RETURN;
    END

    UPDATE dbo.FeeStructures SET Status = N'inactive' WHERE Id = @Id AND TenantId = @TenantId;

    SELECT CAST(1 AS bit) AS Found, Id
    FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_Unpublish;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Publish
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        SELECT CAST(0 AS bit) AS Found, CAST(NULL AS uniqueidentifier) AS Id;
        RETURN;
    END

    UPDATE dbo.FeeStructures SET Status = N'inactive' WHERE TenantId = @TenantId AND LOWER(Status) = N'active';
    UPDATE dbo.FeeStructures SET Status = N'active' WHERE Id = @Id AND TenantId = @TenantId;

    SELECT CAST(1 AS bit) AS Found, Id
    FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;
END;");

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
