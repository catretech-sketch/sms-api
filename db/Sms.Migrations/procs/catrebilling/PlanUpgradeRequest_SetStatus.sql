CREATE OR ALTER PROCEDURE dbo.PlanUpgradeRequest_SetStatus
    @Id uniqueidentifier,
    @Status nvarchar(40),
    @ReviewedByUserId uniqueidentifier = NULL,
    @Notes nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PlanUpgradeRequests SET
        Status = @Status,
        ReviewedByUserId = COALESCE(@ReviewedByUserId, ReviewedByUserId),
        Notes = COALESCE(@Notes, Notes),
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

    EXEC dbo.PlanUpgradeRequest_Get @Id = @Id;
END
