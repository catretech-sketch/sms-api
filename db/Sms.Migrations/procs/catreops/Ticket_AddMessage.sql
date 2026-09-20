CREATE OR ALTER PROCEDURE dbo.Ticket_AddMessage
    @TicketId uniqueidentifier, @Who nvarchar(120), @Role nvarchar(20), @Text nvarchar(4000)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.TicketMessages (TicketId, Who, Role, [Text])
    VALUES (@TicketId, @Who, ISNULL(@Role, 'agent'), @Text);

    UPDATE dbo.Tickets SET MessagesCount = MessagesCount + 1, Updated = SYSUTCDATETIME() WHERE Id = @TicketId;
END
