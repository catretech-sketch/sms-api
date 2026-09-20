CREATE OR ALTER PROCEDURE dbo.FuelLog_Create
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @RecordedByUserId uniqueidentifier,
    @OdometerKm int, @FuelAddedLiters decimal(10, 2)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FuelLogs
        (Id, TenantId, BusId, RecordedByUserId, OdometerKm, FuelAddedLiters)
    VALUES
        (@Id, @TenantId, @BusId, @RecordedByUserId, @OdometerKm, @FuelAddedLiters);

    SELECT Id, TenantId, BusId, RecordedByUserId, OdometerKm, FuelAddedLiters, RecordedAt
    FROM dbo.FuelLogs WHERE Id = @Id;
END
