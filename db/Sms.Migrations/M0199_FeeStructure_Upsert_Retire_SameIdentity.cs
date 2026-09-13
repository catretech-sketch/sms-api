using FluentMigrator;

namespace Sms.Migrations;

/// M0197 made publishing/unpublishing an *explicit* version (by Id) never touch any other
/// version's status, so unrelated fee structures (e.g. Transport vs Exam) can be independently
/// Published. But the no-Id "quick toggle" insert path (used by PUT /v1/fees/structure without
/// an Id) never retired anything either — so saving the *same* fee structure (identical Name /
/// AcademicYear / ClassGrade / Section) as inactive, then active again, left the original active
/// row active the whole time, and a genuine reactivation never looked like one.
/// Fix: only the no-Id insert path now retires prior active row(s) that share the exact same
/// identity (Name, AcademicYear, ClassGrade, Section) before inserting the new version — the
/// same conceptual fee structure never has more than one active row. Distinct fee structures
/// (different identity) are completely unaffected and keep publishing independently.
[Migration(199, "FeeStructure_Upsert: retire same-identity active rows on no-Id insert")]
public sealed class M0199_FeeStructure_Upsert_Retire_SameIdentity : Migration
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
    -- version. Before inserting, retire any other row with the exact same identity that is
    -- still active — the same conceptual fee structure never has more than one active row.
    -- Rows with a different identity (a genuinely different fee structure) are untouched, so
    -- independent multi-publish keeps working.
    UPDATE dbo.FeeStructures
    SET Status = N'inactive'
    WHERE TenantId = @TenantId
      AND LOWER(Status) = N'active'
      AND Name = @Name
      AND AcademicYear = @AcademicYear
      AND ISNULL(ClassGrade, N'') = ISNULL(@ClassGrade, N'')
      AND ISNULL(Section, N'') = ISNULL(@Section, N'')
      AND (@Id IS NULL OR Id <> @Id);

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

    DECLARE @ExistingStatus nvarchar(20);
    IF @Id IS NOT NULL
        SELECT @ExistingStatus = Status FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;

    IF @ExistingStatus IS NOT NULL AND LOWER(@ExistingStatus) <> N'active'
    BEGIN
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
