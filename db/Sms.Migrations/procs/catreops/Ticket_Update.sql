CREATE OR ALTER PROCEDURE dbo.Ticket_Update
    @Id uniqueidentifier, @Status nvarchar(20), @Assignee nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tickets
    SET Status = ISNULL(@Status, Status), Assignee = COALESCE(@Assignee, Assignee), Updated = SYSUTCDATETIME()
    WHERE Id = @Id;

    SELECT Id, Subject, TenantId, TenantName, Status, Priority, Assignee, Created, Updated, MessagesCount
    FROM dbo.Tickets WHERE Id = @Id;
END
