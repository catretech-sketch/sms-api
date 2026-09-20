CREATE OR ALTER PROCEDURE dbo.Subscription_SetPlan
    @TenantId uniqueidentifier,
    @PlanId uniqueidentifier,
    @Seats int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier =
        (SELECT TOP 1 Id FROM dbo.Subscriptions
         WHERE TenantId = @TenantId AND Status = N'active'
         ORDER BY StartedAt DESC);

    IF @Id IS NULL
    BEGIN
        SET @Id = NEWID();
        INSERT dbo.Subscriptions (Id, TenantId, PlanId, Status, RenewsAt, Seats)
        VALUES (@Id, @TenantId, @PlanId, N'active', DATEADD(month, 1, SYSUTCDATETIME()), ISNULL(@Seats, 0));
    END
    ELSE
    BEGIN
        UPDATE dbo.Subscriptions SET
            PlanId = @PlanId,
            Seats = ISNULL(@Seats, Seats),
            RenewsAt = DATEADD(month, 1, SYSUTCDATETIME())
        WHERE Id = @Id;
    END

    SELECT Id, TenantId, PlanId, Status, StartedAt, RenewsAt, Seats
    FROM dbo.Subscriptions WHERE Id = @Id;
END
