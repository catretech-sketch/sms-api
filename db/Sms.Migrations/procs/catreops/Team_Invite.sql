CREATE OR ALTER PROCEDURE dbo.Team_Invite
    @Name nvarchar(200),
    @Email nvarchar(256),
    @Role nvarchar(20),
    @EmployeeId nvarchar(40) = NULL,
    @PhotoUrl nvarchar(max) = NULL,
    @Phone nvarchar(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.TeamMembers (Id, Name, Email, Role, Status, EmployeeId, PhotoUrl, Phone)
    VALUES (@Id, @Name, @Email, @Role, 'active', @EmployeeId, @PhotoUrl, @Phone);
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined, EmployeeId, PhotoUrl, Phone
    FROM dbo.TeamMembers WHERE Id = @Id;
END
