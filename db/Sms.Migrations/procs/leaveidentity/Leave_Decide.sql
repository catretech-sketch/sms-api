CREATE OR ALTER PROCEDURE dbo.Leave_Decide
    @Id uniqueidentifier, @Status nvarchar(20), @DecidedBy uniqueidentifier, @DecidedNote nvarchar(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LeaveRequests SET Status = @Status, DecidedBy = @DecidedBy, DecidedNote = @DecidedNote WHERE Id = @Id;

    SELECT Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote, Priority, AttachmentUrls
    FROM dbo.LeaveRequests WHERE Id = @Id;
END
