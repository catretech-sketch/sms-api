CREATE OR ALTER PROCEDURE dbo.SchoolLocation_Upsert
    @TenantId uniqueidentifier, @Lat float, @Lng float, @RadiusMeters int, @Name nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.SchoolLocations AS tgt
    USING (SELECT @TenantId AS TenantId) AS src ON tgt.TenantId = src.TenantId
    WHEN MATCHED THEN
        UPDATE SET Lat = @Lat, Lng = @Lng, RadiusMeters = ISNULL(@RadiusMeters, 50), Name = @Name
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, Lat, Lng, RadiusMeters, Name)
        VALUES (NEWID(), @TenantId, @Lat, @Lng, ISNULL(@RadiusMeters, 50), @Name);

    SELECT Lat, Lng, RadiusMeters, Name FROM dbo.SchoolLocations WHERE TenantId = @TenantId;
END
