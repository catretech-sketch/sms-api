CREATE OR ALTER PROCEDURE dbo.PlanUpgradeRequest_Create
    @TenantId uniqueidentifier,
    @FromPlanId uniqueidentifier = NULL,
    @ToPlanId uniqueidentifier,
    @Amount decimal(18,2),
    @Currency nvarchar(8),
    @Mode nvarchar(20),
    @Status nvarchar(40),
    @RequestedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.PlanUpgradeRequests (
        Id, TenantId, FromPlanId, ToPlanId, Amount, Currency, Mode, Status,
        RequestedByUserId, CreatedAt, UpdatedAt)
    VALUES (
        @Id, @TenantId, @FromPlanId, @ToPlanId, @Amount, ISNULL(@Currency, N'INR'), @Mode, @Status,
        @RequestedByUserId, SYSUTCDATETIME(), SYSUTCDATETIME());

    EXEC dbo.PlanUpgradeRequest_Get @Id = @Id;
END
