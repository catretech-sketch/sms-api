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
END
