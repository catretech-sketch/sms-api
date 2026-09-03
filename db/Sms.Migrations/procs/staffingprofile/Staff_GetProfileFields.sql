CREATE OR ALTER PROCEDURE dbo.Staff_GetProfileFields
    @TenantId uniqueidentifier, @UserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    -- Same identity-join pattern as StaffDocuments_ListForUser: resolve the caller's own
    -- Staff row from their login identity. No matching row (wrong role, not yet linked)
    -- simply yields zero rows, never an error.
    SELECT s.LicenseNumber, s.LicenseExpiry, s.EmergencyContactName, s.EmergencyContactPhone
    FROM dbo.Staff s
    WHERE s.UserId = @UserId AND s.TenantId = @TenantId;
END
