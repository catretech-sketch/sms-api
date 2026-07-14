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
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END
