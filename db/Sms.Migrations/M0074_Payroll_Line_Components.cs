using FluentMigrator;

namespace Sms.Migrations;

[Migration(74, "Payroll: freeze the detailed salary-component breakdown (basic/hra/allowances/epf/prof-tax/other) on each PayrollRunLine so payslips stay accurate after a run")]
public sealed class M0074_Payroll_Line_Components : Migration
{
    public override void Up()
    {
        Alter.Table("PayrollRunLines")
            .AddColumn("Basic").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Hra").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Allowances").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Epf").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("ProfTax").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("OtherDeductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0);

        // Refresh the save proc so it persists the component breakdown from the lines payload.
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
        INSERT dbo.PayrollRunLines (TenantId, RunId, PersonType, PersonId, Name, Role, Dept, Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions, Gross, Deductions, Net)
        SELECT @TenantId, @RunId, j.PersonType, j.PersonId, j.Name, j.Role, j.Dept,
               j.Basic, j.Hra, j.Allowances, j.Epf, j.ProfTax, j.OtherDeductions, j.Gross, j.Deductions, j.Net
        FROM OPENJSON(@Lines) WITH (
            PersonType nvarchar(10) '$.personType',
            PersonId uniqueidentifier '$.personId',
            Name nvarchar(200) '$.name',
            Role nvarchar(120) '$.role',
            Dept nvarchar(120) '$.dept',
            Basic decimal(18,2) '$.basic',
            Hra decimal(18,2) '$.hra',
            Allowances decimal(18,2) '$.allowances',
            Epf decimal(18,2) '$.epf',
            ProfTax decimal(18,2) '$.profTax',
            OtherDeductions decimal(18,2) '$.otherDeductions',
            Gross decimal(18,2) '$.gross',
            Deductions decimal(18,2) '$.deductions',
            Net decimal(18,2) '$.net'
        ) j;

    SELECT Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net,
           RunBy, RunAt, ApprovedBy, ApprovedAt
    FROM dbo.PayrollRuns WHERE Id=@RunId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PayrollRunLine_ListByPeriod
    @TenantId uniqueidentifier, @Period nvarchar(7)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.PersonType, l.PersonId, l.Name, l.Role, l.Dept,
           l.Basic, l.Hra, l.Allowances, l.Epf, l.ProfTax, l.OtherDeductions,
           l.Gross, l.Deductions, l.Net
    FROM dbo.PayrollRunLines l
    JOIN dbo.PayrollRuns r ON r.Id = l.RunId AND r.TenantId = l.TenantId
    WHERE r.TenantId=@TenantId AND r.Period=@Period
    ORDER BY l.Net DESC;
END");
    }

    public override void Down()
    {
        // Restore the pre-component save/list procs.
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

        Delete.Column("Basic").Column("Hra").Column("Allowances").Column("Epf").Column("ProfTax").Column("OtherDeductions").FromTable("PayrollRunLines");
    }
}
