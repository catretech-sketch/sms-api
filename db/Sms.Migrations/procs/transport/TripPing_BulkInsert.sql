CREATE OR ALTER PROCEDURE dbo.TripPing_BulkInsert
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @Rows dbo.TripPingTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At, Accuracy)
    SELECT NEWID(), @TenantId, @TripId, Lat, Lng, SpeedKmh, Heading, At, Accuracy FROM @Rows;
END
