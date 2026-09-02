CREATE OR ALTER PROCEDURE dbo.Leave_Decide
    @Id uniqueidentifier, @Status nvarchar(20), @DecidedBy uniqueidentifier, @DecidedNote nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LeaveRequests SET Status = @Status, DecidedBy = @DecidedBy, DecidedNote = @DecidedNote WHERE Id = @Id;

    -- 16 columns: the 14 LeaveResponse fields + RequesterName + DecidedByName.
    -- Do not SELECT leftover columns like Note — Dapper binds by constructor arity.
    SELECT lr.Id, lr.TenantId, lr.RequesterId, lr.ChildId, lr.Type, lr.FromDate, lr.ToDate,
           lr.Reason, lr.Substitute, lr.Status, lr.AppliedOn, lr.DecidedNote, lr.Priority, lr.AttachmentUrls,
           u.Name AS RequesterName, d.Name AS DecidedByName
    FROM dbo.LeaveRequests lr
    LEFT JOIN dbo.Users u ON u.Id = lr.RequesterId
    LEFT JOIN dbo.Users d ON d.Id = lr.DecidedBy
    WHERE lr.Id = @Id;
END
