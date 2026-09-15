CREATE OR ALTER PROCEDURE dbo.Issue_Update
    @Id uniqueidentifier, @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Issues SET Status = @Status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;

    SELECT Id, TenantId, ReporterUserId, Category, Title, Description, Priority, Status,
           VehicleId, RouteId, TripId, PhotoUrl, CreatedAt, UpdatedAt
    FROM dbo.Issues WHERE Id = @Id;
END
