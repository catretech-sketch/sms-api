CREATE OR ALTER PROCEDURE dbo.StaffDocuments_ListForUser
    @TenantId uniqueidentifier, @UserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    -- Resolve the caller's own Staff row from their login identity (Staff.UserId), same
    -- identity-join pattern Trip_Start uses for conductors. A caller with no matching Staff
    -- row (wrong role, not yet linked) or no documents yet simply gets zero rows back —
    -- never an error, since "not a staff member" and "no documents" look the same to the caller.
    SELECT d.Id, d.Label, d.Value, d.Ok
    FROM dbo.StaffDocuments d
    INNER JOIN dbo.Staff s ON s.Id = d.StaffId
    WHERE s.UserId = @UserId AND s.TenantId = @TenantId AND d.TenantId = @TenantId
    ORDER BY d.CreatedAt;
END
