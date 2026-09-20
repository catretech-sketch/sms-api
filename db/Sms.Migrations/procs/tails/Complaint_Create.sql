CREATE OR ALTER PROCEDURE dbo.Complaint_Create
    @TenantId uniqueidentifier, @Subject nvarchar(200), @From nvarchar(120), @Category nvarchar(40),
    @Priority nvarchar(10), @Body nvarchar(2000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.Complaints (Id, TenantId, Subject, [From], Category, Priority, Body)
    VALUES (@Id, @TenantId, @Subject, @From, @Category, ISNULL(@Priority, 'medium'), @Body);

    SELECT Id, TenantId, Subject, [From], Category, Priority, Status, Age, Assignee, Body
    FROM dbo.Complaints WHERE Id = @Id;
END
