using FluentMigrator;

namespace Sms.Migrations;

[Migration(59, "Tenants.LogoUrl for school branding across CRM modules")]
public sealed class M0059_Tenant_LogoUrl : Migration
{
    public override void Up()
    {
        Alter.Table("Tenants").AddColumn("LogoUrl").AsString(int.MaxValue).Nullable();

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Country nvarchar(120),
    @ContactName nvarchar(200), @ContactEmail nvarchar(256), @ContactPhone nvarchar(40),
    @Address nvarchar(300), @PlanId uniqueidentifier, @Csm nvarchar(120),
    @LogoUrl nvarchar(max) = NULL
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
        Address, LogoUrl, Csm, HealthScore)
    VALUES (@Id, @Name, @Slug, 'trial', @Tier, @Country, @PlanId, @PlanName, ISNULL(@Mrr, 0),
        @LS, @LSt, @LStor, @ContactName, @ContactEmail, @ContactPhone,
        @Address, @LogoUrl, @Csm, 100);

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl
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
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl
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
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Delete.Column("LogoUrl").FromTable("Tenants");
        // Leave Client_* procs as Address-only baseline from M0046 semantics via re-run of address procs if needed.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Country nvarchar(120),
    @ContactName nvarchar(200), @ContactEmail nvarchar(256), @ContactPhone nvarchar(40),
    @Address nvarchar(300), @PlanId uniqueidentifier, @Csm nvarchar(120)
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
        LimitsStudents, LimitsStaff, LimitsStorageGb, ContactName, ContactEmail, ContactPhone, Address, Csm, HealthScore)
    VALUES (@Id, @Name, @Slug, 'trial', @Tier, @Country, @PlanId, @PlanName, ISNULL(@Mrr, 0),
        @LS, @LSt, @LStor, @ContactName, @ContactEmail, @ContactPhone, @Address, @Csm, 100);
    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }
}
