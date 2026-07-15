CREATE OR ALTER PROCEDURE dbo.Team_Update
    @Id uniqueidentifier, @Role nvarchar(20), @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TeamMembers SET Role = ISNULL(@Role, Role), Status = ISNULL(@Status, Status) WHERE Id = @Id;
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined FROM dbo.TeamMembers WHERE Id = @Id;
END
