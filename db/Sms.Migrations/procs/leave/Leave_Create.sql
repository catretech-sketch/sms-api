CREATE OR ALTER PROCEDURE dbo.Leave_Create
    @TenantId uniqueidentifier, @RequesterId uniqueidentifier, @ChildId uniqueidentifier, @Type nvarchar(20),
    @FromDate date, @ToDate date, @Reason nvarchar(500), @Substitute nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.LeaveRequests (Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, AppliedOn)
    VALUES (@Id, @TenantId, @RequesterId, @ChildId, ISNULL(@Type, 'casual'), @FromDate, @ToDate, @Reason,
        @Substitute, CAST(SYSUTCDATETIME() AS date));

    SELECT Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote
    FROM dbo.LeaveRequests WHERE Id = @Id;
END
