CREATE OR ALTER PROCEDURE dbo.IssueNote_Add
    @TenantId uniqueidentifier, @IssueId uniqueidentifier, @AuthorUserId uniqueidentifier, @Note nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.IssueNotes (Id, TenantId, IssueId, AuthorUserId, Note)
    VALUES (@Id, @TenantId, @IssueId, @AuthorUserId, @Note);

    SELECT Id, IssueId, AuthorUserId, Note, CreatedAt FROM dbo.IssueNotes WHERE Id = @Id;
END
