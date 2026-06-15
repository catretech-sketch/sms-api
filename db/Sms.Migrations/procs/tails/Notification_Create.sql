CREATE OR ALTER PROCEDURE dbo.Notification_Create
    @TenantId uniqueidentifier, @Icon nvarchar(40), @Tone nvarchar(20), @Title nvarchar(200), @Body nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Notifications (Id, TenantId, Icon, Tone, Title, Body)
    VALUES (@Id, @TenantId, @Icon, @Tone, @Title, @Body);

    SELECT Id, TenantId, Icon, Tone, Title, Body, [Time], Unread FROM dbo.Notifications WHERE Id = @Id;
END
