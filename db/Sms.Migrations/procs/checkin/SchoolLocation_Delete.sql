CREATE OR ALTER PROCEDURE dbo.SchoolLocation_Delete
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.SchoolLocations WHERE TenantId = @TenantId;
END
