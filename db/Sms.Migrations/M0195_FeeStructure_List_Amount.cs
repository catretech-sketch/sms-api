using FluentMigrator;

namespace Sms.Migrations;

/// Adds AmountsJson to FeeStructure_List's result set so the app can show a quick per-version
/// total on the Saved versions list, without a "View" click — the list stays otherwise light
/// (no per-class/per-head breakdown; that's still only in the single-version GET).
[Migration(195, "Fees: FeeStructure_List returns AmountsJson for a summed total")]
public sealed class M0195_FeeStructure_List_Amount : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeStructure_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, AcademicYear, ClassGrade, Section, Currency,
           EffectiveFrom, EffectiveTo, Status, Description, CreatedAt, AmountsJson
    FROM dbo.FeeStructures
    ORDER BY CreatedAt DESC, Id DESC;
END;");
    }

    public override void Down()
    {
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
}
