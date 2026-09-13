using FluentMigrator;

namespace Sms.Migrations;

/// Draft/Published workflow for Fee Structure versions. A save always creates a new "draft"
/// (Status = inactive) unless it's explicitly published (Status = active) — and publishing one
/// version always un-publishes whichever was previously the tenant's one live version, so exactly
/// one version is ever "active" (the one GenerateInvoicesAsync/GET fees/structure actually uses).
/// A draft that has never been published can be deleted outright; the currently-published
/// version cannot — publish something else first, which naturally retires it back to inactive.
[Migration(194, "Fees: FeeStructure_Publish/_Delete, Upsert enforces single-active-version")]
public sealed class M0194_FeeStructure_PublishDelete : Migration
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
    -- Every call inserts a new, immutable version — @Id is accepted (for API back-compat)
    -- but no longer used to locate a row to overwrite. Publishing (Status = active) always
    -- retires whichever version was previously the tenant's one live version.
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
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Delete
    @TenantId uniqueidentifier,
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Status nvarchar(20);
    SELECT @Status = Status FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;

    IF @Status IS NULL
    BEGIN
        SELECT CAST(0 AS bit) AS Deleted, N'not_found' AS Reason;
        RETURN;
    END
    IF LOWER(@Status) = N'active'
    BEGIN
        SELECT CAST(0 AS bit) AS Deleted, N'is_active' AS Reason;
        RETURN;
    END

    DELETE FROM dbo.FeeStructures WHERE Id = @Id AND TenantId = @TenantId;
    SELECT CAST(1 AS bit) AS Deleted, CAST(NULL AS nvarchar(20)) AS Reason;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_Delete;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_Publish;");

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
