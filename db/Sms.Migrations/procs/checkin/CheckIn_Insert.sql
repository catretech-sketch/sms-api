CREATE OR ALTER PROCEDURE dbo.CheckIn_Insert
    @TenantId uniqueidentifier, @UserId uniqueidentifier, @Kind nvarchar(3), @At datetime2,
    @Lat float, @Lng float, @AccuracyMeters float, @DistanceMeters float, @Verified bit
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.CheckIns (Id, TenantId, UserId, Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified)
    VALUES (NEWID(), @TenantId, @UserId, @Kind, @At, @Lat, @Lng, @AccuracyMeters, @DistanceMeters, @Verified);
END
