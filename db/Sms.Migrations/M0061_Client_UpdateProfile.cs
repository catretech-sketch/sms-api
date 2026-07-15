using FluentMigrator;

namespace Sms.Migrations;

[Migration(61, "Client_UpdateProfile — school owner/admin can edit name, address, logo, image, contact")]
public sealed class M0061_Client_UpdateProfile : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_UpdateProfile
    @Id uniqueidentifier,
    @Name nvarchar(200) = NULL,
    @Country nvarchar(120) = NULL,
    @Address nvarchar(300) = NULL,
    @ContactName nvarchar(200) = NULL,
    @ContactEmail nvarchar(256) = NULL,
    @ContactPhone nvarchar(40) = NULL,
    @LogoUrl nvarchar(max) = NULL,
    @ImageUrl nvarchar(max) = NULL,
    @SetLogo bit = 0,
    @SetImage bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Tenants SET
        Name = COALESCE(@Name, Name),
        Country = COALESCE(@Country, Country),
        Address = COALESCE(@Address, Address),
        ContactName = COALESCE(@ContactName, ContactName),
        ContactEmail = COALESCE(@ContactEmail, ContactEmail),
        ContactPhone = COALESCE(@ContactPhone, ContactPhone),
        LogoUrl = CASE WHEN @SetLogo = 1 THEN @LogoUrl ELSE LogoUrl END,
        ImageUrl = CASE WHEN @SetImage = 1 THEN @ImageUrl ELSE ImageUrl END
    WHERE Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl, ImageUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Client_UpdateProfile;");
    }
}
