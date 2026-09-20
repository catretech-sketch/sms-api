using FluentMigrator;

namespace Sms.Migrations;

[Migration(73, "Payroll: detailed salary components (HRA/allowances/prof-tax/other) on SalaryProfiles + SalaryStructures templates keyed by role/designation, RLS + procs")]
public sealed class M0073_Salary_Structure : Migration
{
    public override void Up()
    {
        // ---- Extend the per-person salary master with detailed components ----
        Alter.Table("SalaryProfiles")
            .AddColumn("Hra").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Allowances").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("ProfTax").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("OtherDeductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0);

        // ---- Salary structure templates: one row per (tenant, personType, roleKey) ----
        // roleKey = teacher designation ('Teacher','HOD',…) or staff role ('Driver','Clerk',…).
        Create.Table("SalaryStructures")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("PersonType").AsString(10).NotNullable() // 'teacher' | 'staff'
            .WithColumn("RoleKey").AsString(120).NotNullable()
            .WithColumn("Basic").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Hra").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Allowances").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Epf").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("ProfTax").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("OtherDeductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("UX_SalaryStructures_Role").OnTable("SalaryStructures")
            .OnColumn("TenantId").Ascending()
            .OnColumn("PersonType").Ascending()
            .OnColumn("RoleKey").Ascending()
            .WithOptions().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.SalaryStructuresTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SalaryStructures,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SalaryStructures AFTER INSERT
WITH (STATE = ON);");

        // ---- Refresh the SalaryProfile procs to carry the new detailed columns ----
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryProfile_Upsert
    @TenantId uniqueidentifier, @PersonType nvarchar(10), @PersonId uniqueidentifier,
    @BasicSalary decimal(18,2), @Hra decimal(18,2), @Allowances decimal(18,2),
    @Epf decimal(18,2), @ProfTax decimal(18,2), @OtherDeductions decimal(18,2), @Uan nvarchar(40),
    @BankHolder nvarchar(120), @BankAccount nvarchar(40), @BankName nvarchar(120),
    @Ifsc nvarchar(20), @BankBranch nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.SalaryProfiles
       SET BasicSalary=ISNULL(@BasicSalary,0), Hra=ISNULL(@Hra,0), Allowances=ISNULL(@Allowances,0),
           Epf=ISNULL(@Epf,0), ProfTax=ISNULL(@ProfTax,0), OtherDeductions=ISNULL(@OtherDeductions,0),
           Uan=@Uan, BankHolder=@BankHolder, BankAccount=@BankAccount, BankName=@BankName,
           Ifsc=@Ifsc, BankBranch=@BankBranch, UpdatedAt=SYSUTCDATETIME()
     WHERE TenantId=@TenantId AND PersonType=@PersonType AND PersonId=@PersonId;
    IF @@ROWCOUNT = 0
        INSERT dbo.SalaryProfiles (TenantId, PersonType, PersonId, BasicSalary, Hra, Allowances, Epf, ProfTax, OtherDeductions, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch)
        VALUES (@TenantId, @PersonType, @PersonId, ISNULL(@BasicSalary,0), ISNULL(@Hra,0), ISNULL(@Allowances,0), ISNULL(@Epf,0), ISNULL(@ProfTax,0), ISNULL(@OtherDeductions,0), @Uan, @BankHolder, @BankAccount, @BankName, @Ifsc, @BankBranch);
    SELECT TenantId, PersonType, PersonId, BasicSalary, Hra, Allowances, Epf, ProfTax, OtherDeductions, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch
    FROM dbo.SalaryProfiles WHERE TenantId=@TenantId AND PersonType=@PersonType AND PersonId=@PersonId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryProfile_List
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TenantId, PersonType, PersonId, BasicSalary, Hra, Allowances, Epf, ProfTax, OtherDeductions, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch
    FROM dbo.SalaryProfiles WHERE TenantId=@TenantId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryStructure_Upsert
    @TenantId uniqueidentifier, @PersonType nvarchar(10), @RoleKey nvarchar(120),
    @Basic decimal(18,2), @Hra decimal(18,2), @Allowances decimal(18,2),
    @Epf decimal(18,2), @ProfTax decimal(18,2), @OtherDeductions decimal(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.SalaryStructures
       SET Basic=ISNULL(@Basic,0), Hra=ISNULL(@Hra,0), Allowances=ISNULL(@Allowances,0),
           Epf=ISNULL(@Epf,0), ProfTax=ISNULL(@ProfTax,0), OtherDeductions=ISNULL(@OtherDeductions,0),
           UpdatedAt=SYSUTCDATETIME()
     WHERE TenantId=@TenantId AND PersonType=@PersonType AND RoleKey=@RoleKey;
    IF @@ROWCOUNT = 0
        INSERT dbo.SalaryStructures (TenantId, PersonType, RoleKey, Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions)
        VALUES (@TenantId, @PersonType, @RoleKey, ISNULL(@Basic,0), ISNULL(@Hra,0), ISNULL(@Allowances,0), ISNULL(@Epf,0), ISNULL(@ProfTax,0), ISNULL(@OtherDeductions,0));
    SELECT TenantId, PersonType, RoleKey, Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions
    FROM dbo.SalaryStructures WHERE TenantId=@TenantId AND PersonType=@PersonType AND RoleKey=@RoleKey;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryStructure_List
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TenantId, PersonType, RoleKey, Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions
    FROM dbo.SalaryStructures WHERE TenantId=@TenantId;
END");
    }

    public override void Down()
    {
        foreach (var p in new[] { "SalaryStructure_Upsert", "SalaryStructure_List" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{p};");

        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.SalaryStructuresTenantPolicy;");
        Delete.Table("SalaryStructures");

        Delete.Column("Hra").Column("Allowances").Column("ProfTax").Column("OtherDeductions").FromTable("SalaryProfiles");

        // Restore the original (pre-detailed) SalaryProfile procs.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryProfile_Upsert
    @TenantId uniqueidentifier, @PersonType nvarchar(10), @PersonId uniqueidentifier,
    @BasicSalary decimal(18,2), @Epf decimal(18,2), @Uan nvarchar(40),
    @BankHolder nvarchar(120), @BankAccount nvarchar(40), @BankName nvarchar(120),
    @Ifsc nvarchar(20), @BankBranch nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.SalaryProfiles
       SET BasicSalary=ISNULL(@BasicSalary,0), Epf=ISNULL(@Epf,0), Uan=@Uan,
           BankHolder=@BankHolder, BankAccount=@BankAccount, BankName=@BankName,
           Ifsc=@Ifsc, BankBranch=@BankBranch, UpdatedAt=SYSUTCDATETIME()
     WHERE TenantId=@TenantId AND PersonType=@PersonType AND PersonId=@PersonId;
    IF @@ROWCOUNT = 0
        INSERT dbo.SalaryProfiles (TenantId, PersonType, PersonId, BasicSalary, Epf, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch)
        VALUES (@TenantId, @PersonType, @PersonId, ISNULL(@BasicSalary,0), ISNULL(@Epf,0), @Uan, @BankHolder, @BankAccount, @BankName, @Ifsc, @BankBranch);
    SELECT TenantId, PersonType, PersonId, BasicSalary, Epf, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch
    FROM dbo.SalaryProfiles WHERE TenantId=@TenantId AND PersonType=@PersonType AND PersonId=@PersonId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SalaryProfile_List
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TenantId, PersonType, PersonId, BasicSalary, Epf, Uan, BankHolder, BankAccount, BankName, Ifsc, BankBranch
    FROM dbo.SalaryProfiles WHERE TenantId=@TenantId;
END");
    }
}
