using FluentMigrator;

namespace Sms.Migrations;

[Migration(72, "Payroll: SalaryProfiles (salary master), PayrollRuns + PayrollRunLines (monthly cycle with run/approve), RLS + procs")]
public sealed class M0072_Payroll : Migration
{
    public override void Up()
    {
        // ---- Salary master: one row per teacher/staff person ----
        Create.Table("SalaryProfiles")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("PersonType").AsString(10).NotNullable() // 'teacher' | 'staff'
            .WithColumn("PersonId").AsGuid().NotNullable()
            .WithColumn("BasicSalary").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Epf").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Uan").AsString(40).Nullable()
            .WithColumn("BankHolder").AsString(120).Nullable()
            .WithColumn("BankAccount").AsString(40).Nullable()
            .WithColumn("BankName").AsString(120).Nullable()
            .WithColumn("Ifsc").AsString(20).Nullable()
            .WithColumn("BankBranch").AsString(120).Nullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("UX_SalaryProfiles_Person").OnTable("SalaryProfiles")
            .OnColumn("TenantId").Ascending()
            .OnColumn("PersonType").Ascending()
            .OnColumn("PersonId").Ascending()
            .WithOptions().Unique();

        // ---- Payroll run header: one row per tenant per period (YYYY-MM) ----
        Create.Table("PayrollRuns")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Period").AsString(7).NotNullable() // 'YYYY-MM'
            .WithColumn("Year").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Month").AsString(20).Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("run") // run | approved
            .WithColumn("StaffCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Gross").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Deductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Net").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("RunBy").AsGuid().Nullable()
            .WithColumn("RunAt").AsDateTime2().Nullable()
            .WithColumn("ApprovedBy").AsGuid().Nullable()
            .WithColumn("ApprovedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("UX_PayrollRuns_Period").OnTable("PayrollRuns")
            .OnColumn("TenantId").Ascending().OnColumn("Period").Ascending().WithOptions().Unique();

        // ---- Payroll run lines: frozen per-person snapshot at run time ----
        Create.Table("PayrollRunLines")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RunId").AsGuid().NotNullable()
            .WithColumn("PersonType").AsString(10).NotNullable()
            .WithColumn("PersonId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Role").AsString(120).Nullable()
            .WithColumn("Dept").AsString(120).Nullable()
            .WithColumn("Gross").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Deductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Net").AsDecimal(18, 2).NotNullable().WithDefaultValue(0);
        Create.Index("IX_PayrollRunLines_Run").OnTable("PayrollRunLines")
            .OnColumn("TenantId").Ascending().OnColumn("RunId").Ascending();

        foreach (var t in new[] { "SalaryProfiles", "PayrollRuns", "PayrollRunLines" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");

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

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PayrollRun_Get
    @TenantId uniqueidentifier, @Period nvarchar(7)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net,
           RunBy, RunAt, ApprovedBy, ApprovedAt
    FROM dbo.PayrollRuns WHERE TenantId=@TenantId AND Period=@Period;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PayrollRunLine_ListByPeriod
    @TenantId uniqueidentifier, @Period nvarchar(7)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.PersonType, l.PersonId, l.Name, l.Role, l.Dept, l.Gross, l.Deductions, l.Net
    FROM dbo.PayrollRunLines l
    JOIN dbo.PayrollRuns r ON r.Id = l.RunId AND r.TenantId = l.TenantId
    WHERE r.TenantId=@TenantId AND r.Period=@Period
    ORDER BY l.Net DESC;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PayrollRun_Save
    @TenantId uniqueidentifier, @Period nvarchar(7), @Year int, @Month nvarchar(20),
    @StaffCount int, @Gross decimal(18,2), @Deductions decimal(18,2), @Net decimal(18,2),
    @RunBy uniqueidentifier, @Lines nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RunId uniqueidentifier;
    SELECT @RunId = Id FROM dbo.PayrollRuns WHERE TenantId=@TenantId AND Period=@Period;

    IF @RunId IS NULL
    BEGIN
        SET @RunId = NEWID();
        INSERT dbo.PayrollRuns (Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net, RunBy, RunAt)
        VALUES (@RunId, @TenantId, @Period, ISNULL(@Year,0), @Month, 'run', ISNULL(@StaffCount,0),
                ISNULL(@Gross,0), ISNULL(@Deductions,0), ISNULL(@Net,0), @RunBy, SYSUTCDATETIME());
    END
    ELSE
    BEGIN
        UPDATE dbo.PayrollRuns
           SET Year=ISNULL(@Year,0), Month=@Month, Status='run', StaffCount=ISNULL(@StaffCount,0),
               Gross=ISNULL(@Gross,0), Deductions=ISNULL(@Deductions,0), Net=ISNULL(@Net,0),
               RunBy=@RunBy, RunAt=SYSUTCDATETIME(), ApprovedBy=NULL, ApprovedAt=NULL
         WHERE Id=@RunId;
        DELETE FROM dbo.PayrollRunLines WHERE RunId=@RunId AND TenantId=@TenantId;
    END

    IF @Lines IS NOT NULL AND LEN(@Lines) > 2
        INSERT dbo.PayrollRunLines (TenantId, RunId, PersonType, PersonId, Name, Role, Dept, Gross, Deductions, Net)
        SELECT @TenantId, @RunId, j.PersonType, j.PersonId, j.Name, j.Role, j.Dept, j.Gross, j.Deductions, j.Net
        FROM OPENJSON(@Lines) WITH (
            PersonType nvarchar(10) '$.personType',
            PersonId uniqueidentifier '$.personId',
            Name nvarchar(200) '$.name',
            Role nvarchar(120) '$.role',
            Dept nvarchar(120) '$.dept',
            Gross decimal(18,2) '$.gross',
            Deductions decimal(18,2) '$.deductions',
            Net decimal(18,2) '$.net'
        ) j;

    SELECT Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net,
           RunBy, RunAt, ApprovedBy, ApprovedAt
    FROM dbo.PayrollRuns WHERE Id=@RunId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PayrollRun_Approve
    @TenantId uniqueidentifier, @Period nvarchar(7), @ApprovedBy uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PayrollRuns
       SET Status='approved', ApprovedBy=@ApprovedBy, ApprovedAt=SYSUTCDATETIME()
     WHERE TenantId=@TenantId AND Period=@Period AND Status='run';
    SELECT Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net,
           RunBy, RunAt, ApprovedBy, ApprovedAt
    FROM dbo.PayrollRuns WHERE TenantId=@TenantId AND Period=@Period;
END");
    }

    public override void Down()
    {
        foreach (var p in new[]
        {
            "SalaryProfile_Upsert", "SalaryProfile_List", "PayrollRun_Get",
            "PayrollRunLine_ListByPeriod", "PayrollRun_Save", "PayrollRun_Approve",
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{p};");

        foreach (var t in new[] { "PayrollRunLines", "PayrollRuns", "SalaryProfiles" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");

        Delete.Table("PayrollRunLines");
        Delete.Table("PayrollRuns");
        Delete.Table("SalaryProfiles");
    }
}
