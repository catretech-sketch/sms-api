CREATE OR ALTER PROCEDURE dbo.Subscription_Create
    @TenantId uniqueidentifier, @PlanId uniqueidentifier, @Seats int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Subscriptions (Id, TenantId, PlanId, Status, RenewsAt, Seats)
    VALUES (@Id, @TenantId, @PlanId, 'active', DATEADD(month, 1, SYSUTCDATETIME()), @Seats);
    SELECT Id, TenantId, PlanId, Status, StartedAt, RenewsAt, Seats
    FROM dbo.Subscriptions WHERE Id = @Id;
END
