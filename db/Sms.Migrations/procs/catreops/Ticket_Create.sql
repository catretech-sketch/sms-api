CREATE OR ALTER PROCEDURE dbo.Ticket_Create
    @Subject nvarchar(300), @TenantId uniqueidentifier, @TenantName nvarchar(200), @Priority nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Tickets (Id, Subject, TenantId, TenantName, Status, Priority)
    VALUES (@Id, @Subject, @TenantId, @TenantName, 'open', ISNULL(@Priority, 'normal'));
    SELECT @Id AS Id;
END
