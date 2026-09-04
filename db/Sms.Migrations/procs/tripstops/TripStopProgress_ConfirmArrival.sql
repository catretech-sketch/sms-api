CREATE OR ALTER PROCEDURE dbo.TripStopProgress_ConfirmArrival
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @StopId uniqueidentifier,
    @Seq int, @ArrivedAt datetime2, @ConfirmedAt datetime2
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.TripStopProgress AS tgt
    USING (SELECT @TripId AS TripId, @StopId AS StopId) AS src
        ON tgt.TenantId = @TenantId AND tgt.TripId = src.TripId AND tgt.StopId = src.StopId
    WHEN MATCHED THEN
        UPDATE SET ArrivedAt = ISNULL(tgt.ArrivedAt, @ArrivedAt), ConfirmedAt = @ConfirmedAt
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, TripId, StopId, Seq, ArrivedAt, ConfirmedAt)
        VALUES (NEWID(), @TenantId, @TripId, @StopId, @Seq, @ArrivedAt, @ConfirmedAt);

    UPDATE dbo.Trips SET CurrentStopId = @StopId WHERE Id = @TripId AND TenantId = @TenantId;
END
