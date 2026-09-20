CREATE OR ALTER PROCEDURE dbo.TripStopProgress_Complete
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @StopId uniqueidentifier, @DepartedAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TripStopProgress SET DepartedAt = @DepartedAt
    WHERE TenantId = @TenantId AND TripId = @TripId AND StopId = @StopId;

    UPDATE dbo.Trips SET CurrentStopId = NULL WHERE Id = @TripId AND TenantId = @TenantId;
END
