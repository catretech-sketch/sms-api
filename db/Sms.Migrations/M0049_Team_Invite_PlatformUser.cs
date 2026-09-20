using FluentMigrator;

namespace Sms.Migrations;

[Migration(49, "Catre: Team_Invite marks members active (login provisioned in TenancyService)")]
public sealed class M0049_Team_Invite_PlatformUser : Migration
{
    public override void Up()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.Team_Invite
    @Name nvarchar(200), @Email nvarchar(256), @Role nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.TeamMembers (Id, Name, Email, Role, Status) VALUES (@Id, @Name, @Email, @Role, 'active');
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined FROM dbo.TeamMembers WHERE Id = @Id;
END
""");
    }

    public override void Down()
    {
        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.Team_Invite
    @Name nvarchar(200), @Email nvarchar(256), @Role nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.TeamMembers (Id, Name, Email, Role, Status) VALUES (@Id, @Name, @Email, @Role, 'invited');
    SELECT Id, Name, Email, Role, Status, LastLogin, Joined FROM dbo.TeamMembers WHERE Id = @Id;
END
""");
    }
}
