using FluentMigrator;

namespace Sms.Migrations;

[Migration(116, "Payslips: store salary component breakdown for mobile payslip screen")]
public sealed class M0116_Payslips_SalaryComponents : Migration
{
    public override void Up()
    {
        Alter.Table("Payslips")
            .AddColumn("Basic").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Hra").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Allowances").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("Epf").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("ProfTax").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("OtherDeductions").AsDecimal(18, 2).NotNullable().WithDefaultValue(0);

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Payslip_Create
    @TenantId uniqueidentifier, @UserId uniqueidentifier, @Month nvarchar(20), @Year int,
    @Gross decimal(18,2), @Deductions decimal(18,2), @Net decimal(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Payslips (Id, TenantId, UserId, Month, Year, Gross, Deductions, Net)
    VALUES (@Id, @TenantId, @UserId, @Month, ISNULL(@Year, 0), ISNULL(@Gross, 0), ISNULL(@Deductions, 0), ISNULL(@Net, 0));

    SELECT Id, TenantId, UserId, Month, Year, Gross, Deductions, Net, Status,
           Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions
    FROM dbo.Payslips WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Delete.Column("Basic").Column("Hra").Column("Allowances")
            .Column("Epf").Column("ProfTax").Column("OtherDeductions").FromTable("Payslips");
    }
}
