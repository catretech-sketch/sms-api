CREATE OR ALTER PROCEDURE dbo.Team_Invite
    @Name nvarchar(200), @Email nvarchar(256), @Role nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.TeamMembers (Id, Name, Email, Role, Status) VALUES (@Id, @Name, @Email, @Role, 'invited');
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined FROM dbo.TeamMembers WHERE Id = @Id;
END
