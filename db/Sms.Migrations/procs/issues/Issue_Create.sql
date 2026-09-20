CREATE OR ALTER PROCEDURE dbo.Issue_Create
    @TenantId uniqueidentifier, @ReporterUserId uniqueidentifier, @Category nvarchar(20),
    @Title nvarchar(200), @Description nvarchar(2000), @Priority nvarchar(20),
    @VehicleId uniqueidentifier = NULL, @RouteId uniqueidentifier = NULL, @TripId uniqueidentifier = NULL,
    @PhotoUrl nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Issues
        (Id, TenantId, ReporterUserId, Category, Title, Description, Priority, VehicleId, RouteId, TripId, PhotoUrl)
    VALUES
        (@Id, @TenantId, @ReporterUserId, @Category, @Title, @Description, @Priority, @VehicleId, @RouteId, @TripId, @PhotoUrl);

    SELECT Id, TenantId, ReporterUserId, Category, Title, Description, Priority, Status,
           VehicleId, RouteId, TripId, PhotoUrl, CreatedAt, UpdatedAt
    FROM dbo.Issues WHERE Id = @Id;
END
