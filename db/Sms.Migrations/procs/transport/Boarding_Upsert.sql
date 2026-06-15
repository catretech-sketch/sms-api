CREATE OR ALTER PROCEDURE dbo.Boarding_Upsert
    @TenantId uniqueidentifier, @TripId uniqueidentifier, @StudentId uniqueidentifier,
    @StopId uniqueidentifier, @State nvarchar(10), @At datetime2
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.Boardings AS tgt
    USING (SELECT @TripId AS TripId, @StudentId AS StudentId) AS src
        ON tgt.TenantId = @TenantId AND tgt.TripId = src.TripId AND tgt.StudentId = src.StudentId
    WHEN MATCHED THEN
        UPDATE SET State = @State, StopId = @StopId, At = @At
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, TripId, StudentId, StopId, State, At)
        VALUES (NEWID(), @TenantId, @TripId, @StudentId, @StopId, ISNULL(@State, 'pending'), @At);
END
