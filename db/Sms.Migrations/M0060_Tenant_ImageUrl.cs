using FluentMigrator;

namespace Sms.Migrations;

[Migration(60, "Tenants.ImageUrl — school campus/cover photo on create & CRM")]
public sealed class M0060_Tenant_ImageUrl : Migration
{
    public override void Up()
    {
        Alter.Table("Tenants").AddColumn("ImageUrl").AsString(int.MaxValue).Nullable();

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Country nvarchar(120),
    @ContactName nvarchar(200), @ContactEmail nvarchar(256), @ContactPhone nvarchar(40),
    @Address nvarchar(300), @PlanId uniqueidentifier, @Csm nvarchar(120),
    @LogoUrl nvarchar(max) = NULL,
    @ImageUrl nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @PlanName nvarchar(100), @Tier nvarchar(20), @Mrr decimal(18,2),
            @LS int, @LSt int, @LStor int;

    SELECT @PlanName = p.Name, @Tier = p.Tier, @Mrr = p.Price,
           @LS = p.LimitsStudents, @LSt = p.LimitsStaff, @LStor = p.LimitsStorageGb
    FROM dbo.Plans p WHERE p.Id = @PlanId;

    INSERT dbo.Tenants (Id, Name, Slug, Status, Tier, Country, PlanId, PlanName, Mrr,
        LimitsStudents, LimitsStaff, LimitsStorageGb, ContactName, ContactEmail, ContactPhone,
        Address, LogoUrl, ImageUrl, Csm, HealthScore)
    VALUES (@Id, @Name, @Slug, 'trial', @Tier, @Country, @PlanId, @PlanName, ISNULL(@Mrr, 0),
        @LS, @LSt, @LStor, @ContactName, @ContactEmail, @ContactPhone,
        @Address, @LogoUrl, @ImageUrl, @Csm, 100);

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl, ImageUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_SetStatus
    @Id uniqueidentifier, @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tenants SET Status = @Status WHERE Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl, ImageUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_ChangePlan
    @Id uniqueidentifier, @PlanId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t SET
        t.PlanId = @PlanId, t.PlanName = p.Name, t.Tier = p.Tier, t.Mrr = p.Price,
        t.LimitsStudents = p.LimitsStudents, t.LimitsStaff = p.LimitsStaff, t.LimitsStorageGb = p.LimitsStorageGb
    FROM dbo.Tenants t
    JOIN dbo.Plans p ON p.Id = @PlanId
    WHERE t.Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl, ImageUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Delete.Column("ImageUrl").FromTable("Tenants");
    }
}
