using FluentMigrator;

namespace Sms.Migrations;

/// Every explicit Save now creates a new, immutable FeeStructures row instead of overwriting
/// the tenant's existing one — the same "never rewrite history" principle already used for
/// invoice lines (M0192). FeeStructure_Get still resolves "the current structure" to edit as
/// the most recent row, so the existing edit/save UX is unchanged; FeeStructure_List is new,
/// letting the UI show every past saved version.
[Migration(193, "Fees: FeeStructures.CreatedAt + save-always-inserts + FeeStructure_List")]
public sealed class M0193_FeeStructure_History : Migration
{
    public override void Up()
    {
        // The backfill UPDATE and the NOT NULL rewrite below must see every tenant's rows, not
        // just whatever the migration connection's (unset) session context would filter to — so
        // the tenant RLS policy is bracketed off/on around the schema change, exactly as if a DBA
        // ran it interactively. This is a maintenance-window operation on a security policy, not
        // an app-level exception.
        Execute.Sql("ALTER SECURITY POLICY rls.FeeStructuresTenantPolicy WITH (STATE = OFF);");

        Execute.Sql(@"
IF COL_LENGTH('dbo.FeeStructures', 'CreatedAt') IS NULL
    ALTER TABLE dbo.FeeStructures ADD CreatedAt datetime2 NULL;");
        Execute.Sql(@"
UPDATE dbo.FeeStructures SET CreatedAt = SYSUTCDATETIME() WHERE CreatedAt IS NULL;");
        Execute.Sql(@"
ALTER TABLE dbo.FeeStructures ALTER COLUMN CreatedAt datetime2 NOT NULL;");
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_FeeStructures_CreatedAt')
    ALTER TABLE dbo.FeeStructures ADD CONSTRAINT DF_FeeStructures_CreatedAt DEFAULT (SYSUTCDATETIME()) FOR CreatedAt;");

        Execute.Sql("ALTER SECURITY POLICY rls.FeeStructuresTenantPolicy WITH (STATE = ON);");

        Create.Index("IX_FeeStructures_Tenant_CreatedAt").OnTable("FeeStructures")
            .OnColumn("TenantId").Ascending().OnColumn("CreatedAt").Descending();

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
    -- but no longer used to locate a row to overwrite.
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
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
        EffectiveFrom, EffectiveTo, Status, Description, AmountsJson, CreatedAt
    FROM dbo.FeeStructures
    ORDER BY
        CASE WHEN LOWER(Status) = N'active' THEN 0 ELSE 1 END,
        CreatedAt DESC,
        Id DESC;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
           EffectiveFrom, EffectiveTo, Status, Description, CreatedAt
    FROM dbo.FeeStructures
    ORDER BY CreatedAt DESC, Id DESC;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_List;");

        // Restore the pre-history Get/Upsert (update-in-place) behavior.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
        EffectiveFrom, EffectiveTo, Status, Description, AmountsJson
    FROM dbo.FeeStructures
    ORDER BY
        CASE WHEN LOWER(Status) = N'active' THEN 0 ELSE 1 END,
        EffectiveFrom DESC,
        Id DESC;
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
    DECLARE @TargetId uniqueidentifier = @Id;

    IF @TargetId IS NOT NULL AND EXISTS (
        SELECT 1 FROM dbo.FeeStructures WHERE Id = @TargetId AND TenantId = @TenantId)
    BEGIN
        UPDATE dbo.FeeStructures
        SET Name = @Name, AcademicYear = @AcademicYear, ClassGrade = @ClassGrade, Section = @Section,
            Currency = @Currency, EffectiveFrom = @EffectiveFrom, EffectiveTo = @EffectiveTo,
            Status = @Status, Description = @Description, AmountsJson = @AmountsJson
        WHERE Id = @TargetId AND TenantId = @TenantId;
    END
    ELSE
    BEGIN
        SELECT TOP 1 @TargetId = Id FROM dbo.FeeStructures
        WHERE TenantId = @TenantId AND LOWER(Status) = N'active'
        ORDER BY EffectiveFrom DESC, Id DESC;

        IF @TargetId IS NOT NULL
        BEGIN
            UPDATE dbo.FeeStructures
            SET Name = @Name, AcademicYear = @AcademicYear, ClassGrade = @ClassGrade, Section = @Section,
                Currency = @Currency, EffectiveFrom = @EffectiveFrom, EffectiveTo = @EffectiveTo,
                Status = @Status, Description = @Description, AmountsJson = @AmountsJson
            WHERE Id = @TargetId AND TenantId = @TenantId;
        END
        ELSE
        BEGIN
            SET @TargetId = NEWID();
            INSERT dbo.FeeStructures (
                Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
                EffectiveFrom, EffectiveTo, Status, Description, AmountsJson)
            VALUES (
                @TargetId, @TenantId, @Name, @AcademicYear, @ClassGrade, @Section, @Currency,
                @EffectiveFrom, @EffectiveTo, @Status, @Description, @AmountsJson);
        END
    END

    SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
           EffectiveFrom, EffectiveTo, Status, Description, AmountsJson
    FROM dbo.FeeStructures WHERE Id = @TargetId;
END;");

        Execute.Sql("DROP INDEX IF EXISTS IX_FeeStructures_Tenant_CreatedAt ON dbo.FeeStructures;");
        Execute.Sql("IF COL_LENGTH('dbo.FeeStructures', 'CreatedAt') IS NOT NULL ALTER TABLE dbo.FeeStructures DROP CONSTRAINT IF EXISTS DF_FeeStructures_CreatedAt;");
        Execute.Sql("IF COL_LENGTH('dbo.FeeStructures', 'CreatedAt') IS NOT NULL ALTER TABLE dbo.FeeStructures DROP COLUMN CreatedAt;");
    }
}
