using FluentMigrator;

namespace Sms.Migrations;

[Migration(123, "Finance: FeeHeads + FeeStructures (GET/POST/PATCH/DELETE /fees/heads, GET/PUT /fees/structure)")]
public sealed class M0123_FeeHeads_Structure : Migration
{
    public override void Up()
    {
        Create.Table("FeeHeads")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Code").AsString(40).Nullable()
            .WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("IsSystem").AsBoolean().NotNullable().WithDefaultValue(false);
        Create.UniqueConstraint("UQ_FeeHeads_Tenant_Name")
            .OnTable("FeeHeads").Columns("TenantId", "Name");
        Create.Index("IX_FeeHeads_Tenant").OnTable("FeeHeads").OnColumn("TenantId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.FeeHeadsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeHeads,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeHeads AFTER INSERT
WITH (STATE = ON);");

        Create.Table("FeeStructures")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("AcademicYear").AsString(20).NotNullable()
            .WithColumn("ClassGrade").AsString(40).Nullable()
            .WithColumn("Section").AsString(20).Nullable()
            .WithColumn("Currency").AsString(10).NotNullable().WithDefaultValue("INR")
            .WithColumn("EffectiveFrom").AsDate().NotNullable()
            .WithColumn("EffectiveTo").AsDate().Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("Description").AsString(int.MaxValue).Nullable()
            .WithColumn("AmountsJson").AsString(int.MaxValue).NotNullable().WithDefaultValue("{}");
        Create.Index("IX_FeeStructures_Tenant_Status").OnTable("FeeStructures")
            .OnColumn("TenantId").Ascending().OnColumn("Status").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.FeeStructuresTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeStructures,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeeStructures AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads
    ORDER BY Name;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Create
    @TenantId uniqueidentifier,
    @Name nvarchar(120),
    @Code nvarchar(40) = NULL,
    @Active bit = 1,
    @IsSystem bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeeHeads (Id, TenantId, Name, Code, Active, IsSystem)
    VALUES (@Id, @TenantId, @Name, @Code, @Active, @IsSystem);
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads WHERE Id = @Id;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Update
    @Id uniqueidentifier,
    @TenantId uniqueidentifier,
    @Name nvarchar(120) = NULL,
    @Code nvarchar(40) = NULL,
    @CodeSpecified bit = 0,
    @Active bit = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FeeHeads
    SET Name = COALESCE(@Name, Name),
        Code = CASE WHEN @CodeSpecified = 1 THEN @Code ELSE Code END,
        Active = COALESCE(@Active, Active)
    WHERE Id = @Id AND TenantId = @TenantId;
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads WHERE Id = @Id AND TenantId = @TenantId;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Delete
    @Id uniqueidentifier,
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.FeeHeads WHERE Id = @Id AND TenantId = @TenantId;
    SELECT @@ROWCOUNT AS Deleted;
END;");

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
        SET Name = @Name,
            AcademicYear = @AcademicYear,
            ClassGrade = @ClassGrade,
            Section = @Section,
            Currency = @Currency,
            EffectiveFrom = @EffectiveFrom,
            EffectiveTo = @EffectiveTo,
            Status = @Status,
            Description = @Description,
            AmountsJson = @AmountsJson
        WHERE Id = @TargetId AND TenantId = @TenantId;
    END
    ELSE
    BEGIN
        SELECT TOP 1 @TargetId = Id
        FROM dbo.FeeStructures
        WHERE TenantId = @TenantId AND LOWER(Status) = N'active'
        ORDER BY EffectiveFrom DESC, Id DESC;

        IF @TargetId IS NOT NULL
        BEGIN
            UPDATE dbo.FeeStructures
            SET Name = @Name,
                AcademicYear = @AcademicYear,
                ClassGrade = @ClassGrade,
                Section = @Section,
                Currency = @Currency,
                EffectiveFrom = @EffectiveFrom,
                EffectiveTo = @EffectiveTo,
                Status = @Status,
                Description = @Description,
                AmountsJson = @AmountsJson
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
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_Upsert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeStructure_Get;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeHead_Delete;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeHead_Update;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeHead_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.FeeHead_List;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeeStructuresTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeeHeadsTenantPolicy;");
        Delete.Table("FeeStructures");
        Delete.Table("FeeHeads");
    }
}
