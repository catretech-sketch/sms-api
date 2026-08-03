using FluentMigrator;

namespace Sms.Migrations;

[Migration(108, "Tenant geofence coordinates (CRM campus location for teacher check-in)")]
public sealed class M0108_Tenant_Geofence : Migration
{
    public override void Up()
    {
        Alter.Table("Tenants").AddColumn("Lat").AsDouble().Nullable();
        Alter.Table("Tenants").AddColumn("Lng").AsDouble().Nullable();
        Alter.Table("Tenants").AddColumn("GeofenceRadiusMeters").AsInt32().Nullable();

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_UpdateProfile
    @Id uniqueidentifier,
    @Name nvarchar(200) = NULL,
    @Slug nvarchar(48) = NULL,
    @Country nvarchar(120) = NULL,
    @Address nvarchar(300) = NULL,
    @ContactName nvarchar(200) = NULL,
    @ContactEmail nvarchar(256) = NULL,
    @ContactPhone nvarchar(40) = NULL,
    @LogoUrl nvarchar(max) = NULL,
    @ImageUrl nvarchar(max) = NULL,
    @SetLogo bit = 0,
    @SetImage bit = 0,
    @Lat float = NULL,
    @Lng float = NULL,
    @GeofenceRadiusMeters int = NULL,
    @SetGeofence bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Tenants SET
        Name = COALESCE(@Name, Name),
        Slug = COALESCE(@Slug, Slug),
        Country = COALESCE(@Country, Country),
        Address = COALESCE(@Address, Address),
        ContactName = COALESCE(@ContactName, ContactName),
        ContactEmail = COALESCE(@ContactEmail, ContactEmail),
        ContactPhone = COALESCE(@ContactPhone, ContactPhone),
        LogoUrl = CASE WHEN @SetLogo = 1 THEN @LogoUrl ELSE LogoUrl END,
        ImageUrl = CASE WHEN @SetImage = 1 THEN @ImageUrl ELSE ImageUrl END,
        Lat = CASE WHEN @SetGeofence = 1 THEN @Lat ELSE Lat END,
        Lng = CASE WHEN @SetGeofence = 1 THEN @Lng ELSE Lng END,
        GeofenceRadiusMeters = CASE WHEN @SetGeofence = 1 THEN @GeofenceRadiusMeters ELSE GeofenceRadiusMeters END
    WHERE Id = @Id;

    IF @SetGeofence = 1 AND @Lat IS NOT NULL AND @Lng IS NOT NULL AND (@Lat <> 0 OR @Lng <> 0)
    BEGIN
        EXEC dbo.SchoolLocation_Upsert
            @TenantId = @Id,
            @Lat = @Lat,
            @Lng = @Lng,
            @RadiusMeters = ISNULL(@GeofenceRadiusMeters, 250),
            @Name = COALESCE(@Name, (SELECT Name FROM dbo.Tenants WHERE Id = @Id));
    END

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address, LogoUrl, ImageUrl
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_UpdateProfile
    @Id uniqueidentifier,
    @Name nvarchar(200) = NULL,
    @Slug nvarchar(48) = NULL,
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
        Slug = COALESCE(@Slug, Slug),
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

        Delete.Column("GeofenceRadiusMeters").FromTable("Tenants");
        Delete.Column("Lng").FromTable("Tenants");
        Delete.Column("Lat").FromTable("Tenants");
    }
}
