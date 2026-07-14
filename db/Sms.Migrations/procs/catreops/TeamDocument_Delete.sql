CREATE OR ALTER PROCEDURE dbo.TeamDocument_Delete
    @Id uniqueidentifier,
    @TeamMemberId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.TeamDocuments WHERE Id = @Id AND TeamMemberId = @TeamMemberId;
    SELECT @@ROWCOUNT AS Affected;
END
