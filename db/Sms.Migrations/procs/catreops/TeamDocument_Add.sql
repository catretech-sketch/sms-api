CREATE OR ALTER PROCEDURE dbo.TeamDocument_Add
    @TeamMemberId uniqueidentifier,
    @Label nvarchar(120),
    @FileName nvarchar(260),
    @ContentType nvarchar(120),
    @SizeBytes int,
    @Content nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.TeamDocuments (Id, TeamMemberId, Label, FileName, ContentType, SizeBytes, Content)
    VALUES (@Id, @TeamMemberId, @Label, @FileName, @ContentType, @SizeBytes, @Content);
    SELECT Id, TeamMemberId, Label, FileName, ContentType, SizeBytes, CreatedAt AS Created
    FROM dbo.TeamDocuments WHERE Id = @Id;
END
