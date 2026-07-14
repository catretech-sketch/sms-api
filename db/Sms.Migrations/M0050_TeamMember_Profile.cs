using FluentMigrator;

namespace Sms.Migrations;

[Migration(50, "Catre: TeamMembers EmployeeId, PhotoUrl, Phone; expand Team_Invite/Update")]
public sealed class M0050_TeamMember_Profile : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF COL_LENGTH('dbo.TeamMembers', 'EmployeeId') IS NULL
  ALTER TABLE dbo.TeamMembers ADD EmployeeId nvarchar(40) NULL;
IF COL_LENGTH('dbo.TeamMembers', 'PhotoUrl') IS NULL
  ALTER TABLE dbo.TeamMembers ADD PhotoUrl nvarchar(max) NULL;
IF COL_LENGTH('dbo.TeamMembers', 'Phone') IS NULL
  ALTER TABLE dbo.TeamMembers ADD Phone nvarchar(40) NULL;
""");

        Execute.Sql("""
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = 'UX_TeamMembers_EmployeeId' AND object_id = OBJECT_ID('dbo.TeamMembers'))
BEGIN
  SET QUOTED_IDENTIFIER ON;
  CREATE UNIQUE INDEX UX_TeamMembers_EmployeeId ON dbo.TeamMembers(EmployeeId)
  WHERE EmployeeId IS NOT NULL;
END
""");

        Execute.Sql("""
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
""");

        Execute.Sql("""
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
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS UX_TeamMembers_EmployeeId ON dbo.TeamMembers;");
        Execute.Sql("""
IF COL_LENGTH('dbo.TeamMembers', 'EmployeeId') IS NOT NULL ALTER TABLE dbo.TeamMembers DROP COLUMN EmployeeId;
IF COL_LENGTH('dbo.TeamMembers', 'PhotoUrl') IS NOT NULL ALTER TABLE dbo.TeamMembers DROP COLUMN PhotoUrl;
IF COL_LENGTH('dbo.TeamMembers', 'Phone') IS NOT NULL ALTER TABLE dbo.TeamMembers DROP COLUMN Phone;
""");
    }
}
