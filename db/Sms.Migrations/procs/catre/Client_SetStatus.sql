CREATE OR ALTER PROCEDURE dbo.Client_SetStatus
    @Id uniqueidentifier, @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tenants SET Status = @Status WHERE Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END
