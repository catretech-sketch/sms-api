CREATE OR ALTER PROCEDURE dbo.Team_Update
    @Id uniqueidentifier,
    @Role nvarchar(20) = NULL,
    @Status nvarchar(20) = NULL,
    @Name nvarchar(200) = NULL,
    @EmployeeId nvarchar(40) = NULL,
    @PhotoUrl nvarchar(max) = NULL,
    @Phone nvarchar(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TeamMembers SET
        Role = ISNULL(@Role, Role),
        Status = ISNULL(@Status, Status),
        Name = ISNULL(@Name, Name),
        EmployeeId = ISNULL(@EmployeeId, EmployeeId),
        PhotoUrl = ISNULL(@PhotoUrl, PhotoUrl),
        Phone = ISNULL(@Phone, Phone)
    WHERE Id = @Id;
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined, EmployeeId, PhotoUrl, Phone
    FROM dbo.TeamMembers WHERE Id = @Id;
END
