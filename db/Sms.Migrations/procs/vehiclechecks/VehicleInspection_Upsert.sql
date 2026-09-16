CREATE OR ALTER PROCEDURE dbo.VehicleInspection_Upsert
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @SubmittedByUserId uniqueidentifier,
    @Brakes bit, @Tyres bit, @Lights bit, @Horn bit,
    @FirstAidKit bit, @FireExtinguisher bit, @EmergencyExit bit, @FuelLevel bit,
    @AllOk bit, @Remarks nvarchar(2000) = NULL, @InspectionDate date
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.VehicleInspections AS target
    USING (SELECT @TenantId AS TenantId, @BusId AS BusId, @InspectionDate AS InspectionDate) AS src
        ON target.TenantId = src.TenantId AND target.BusId = src.BusId AND target.InspectionDate = src.InspectionDate
    WHEN MATCHED THEN
        UPDATE SET
            SubmittedByUserId = @SubmittedByUserId,
            Brakes = @Brakes, Tyres = @Tyres, Lights = @Lights, Horn = @Horn,
            FirstAidKit = @FirstAidKit, FireExtinguisher = @FireExtinguisher,
            EmergencyExit = @EmergencyExit, FuelLevel = @FuelLevel,
            AllOk = @AllOk, Remarks = @Remarks
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, BusId, SubmittedByUserId, Brakes, Tyres, Lights, Horn,
                FirstAidKit, FireExtinguisher, EmergencyExit, FuelLevel, AllOk, Remarks, InspectionDate)
        VALUES (NEWID(), @TenantId, @BusId, @SubmittedByUserId, @Brakes, @Tyres, @Lights, @Horn,
                @FirstAidKit, @FireExtinguisher, @EmergencyExit, @FuelLevel, @AllOk, @Remarks, @InspectionDate);

    SELECT Id, TenantId, BusId, SubmittedByUserId, Brakes, Tyres, Lights, Horn,
           FirstAidKit, FireExtinguisher, EmergencyExit, FuelLevel, AllOk, Remarks, InspectionDate, CreatedAt
    FROM dbo.VehicleInspections
    WHERE TenantId = @TenantId AND BusId = @BusId AND InspectionDate = @InspectionDate;
END
