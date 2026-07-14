using FluentMigrator;

namespace Sms.Migrations;

[Migration(51, "Catre: TeamDocuments for onboarding docs on team members")]
public sealed class M0051_TeamDocuments : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF OBJECT_ID('dbo.TeamDocuments', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.TeamDocuments (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_TeamDocuments PRIMARY KEY,
    TeamMemberId uniqueidentifier NOT NULL,
    Label nvarchar(120) NOT NULL,
    FileName nvarchar(260) NOT NULL,
    ContentType nvarchar(120) NOT NULL,
    SizeBytes int NOT NULL,
    Content nvarchar(max) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TeamDocuments_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_TeamDocuments_Member FOREIGN KEY (TeamMemberId)
      REFERENCES dbo.TeamMembers(Id) ON DELETE CASCADE
  );
  CREATE INDEX IX_TeamDocuments_Member ON dbo.TeamDocuments(TeamMemberId);
END
""");

        Execute.Sql("""
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
""");

        Execute.Sql("""
CREATE OR ALTER PROCEDURE dbo.TeamDocument_Delete
    @Id uniqueidentifier,
    @TeamMemberId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.TeamDocuments WHERE Id = @Id AND TeamMemberId = @TeamMemberId;
    SELECT @@ROWCOUNT AS Affected;
END
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TeamDocument_Add;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TeamDocument_Delete;");
        Execute.Sql("DROP TABLE IF EXISTS dbo.TeamDocuments;");
    }
}
