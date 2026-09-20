CREATE OR ALTER PROCEDURE dbo.Announcement_Create
    @TenantId uniqueidentifier, @Title nvarchar(200), @Body nvarchar(2000), @From nvarchar(120),
    @Role nvarchar(40), @Type nvarchar(20), @Audience nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Announcements (Id, TenantId, Title, Body, [From], Role, Type, Audience)
    VALUES (@Id, @TenantId, @Title, @Body, @From, @Role, ISNULL(@Type, 'info'), @Audience);

    SELECT Id, TenantId, Title, Body, [Date], [From], Role, Type, Pinned, Audience
    FROM dbo.Announcements WHERE Id = @Id;
END
