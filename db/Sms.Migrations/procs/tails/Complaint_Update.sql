CREATE OR ALTER PROCEDURE dbo.Complaint_Update
    @Id uniqueidentifier, @Status nvarchar(20), @Assignee nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Complaints SET Status = ISNULL(@Status, Status), Assignee = COALESCE(@Assignee, Assignee) WHERE Id = @Id;

    SELECT Id, TenantId, Subject, [From], Category, Priority, Status, Age, Assignee, Body
    FROM dbo.Complaints WHERE Id = @Id;
END
