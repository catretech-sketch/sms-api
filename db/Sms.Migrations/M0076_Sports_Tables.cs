using FluentMigrator;

namespace Sms.Migrations;

[Migration(76, "Sports: Teams + Events + Medals master tables with tenant RLS + insert procs")]
public sealed class M0076_Sports_Tables : Migration
{
    public override void Up()
    {
        Create.Table("SportsTeams")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable()
            .WithColumn("Sport").AsString(60).NotNullable()
            .WithColumn("Coach").AsString(120).Nullable()
            .WithColumn("Athletes").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Index("IX_SportsTeams_Tenant").OnTable("SportsTeams").OnColumn("TenantId").Ascending();

        Create.Table("SportsEvents")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("EventDate").AsDate().NotNullable()
            .WithColumn("Venue").AsString(120).Nullable();
        Create.Index("IX_SportsEvents_Tenant").OnTable("SportsEvents").OnColumn("TenantId").Ascending();

        Create.Table("SportsMedals")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Kind").AsString(10).NotNullable()
            .WithColumn("Title").AsString(120).Nullable()
            .WithColumn("Year").AsInt32().NotNullable();
        Create.Index("IX_SportsMedals_Tenant").OnTable("SportsMedals").OnColumn("TenantId").Ascending();

        foreach (var t in new[] { "SportsTeams", "SportsEvents", "SportsMedals" })
            Execute.Sql($@"CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.SportsTeam_Create
    @TenantId uniqueidentifier, @Name nvarchar(80), @Sport nvarchar(60),
    @Coach nvarchar(120) = NULL, @Athletes int = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.SportsTeams (TenantId, Name, Sport, Coach, Athletes)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Name, @Sport, @Coach, @Athletes);
    SELECT Id, TenantId, Name, Sport, Coach, Athletes FROM dbo.SportsTeams WHERE Id = (SELECT Id FROM @ins);
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.SportsEvent_Create
    @TenantId uniqueidentifier, @Name nvarchar(120), @EventDate date, @Venue nvarchar(120) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.SportsEvents (TenantId, Name, EventDate, Venue)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Name, @EventDate, @Venue);
    SELECT Id, TenantId, Name, EventDate, Venue FROM dbo.SportsEvents WHERE Id = (SELECT Id FROM @ins);
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.SportsMedal_Create
    @TenantId uniqueidentifier, @Kind nvarchar(10), @Title nvarchar(120) = NULL, @Year int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.SportsMedals (TenantId, Kind, Title, Year)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Kind, @Title, @Year);
    SELECT Id, TenantId, Kind, Title, Year FROM dbo.SportsMedals WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        foreach (var p in new[] { "SportsTeam_Create", "SportsEvent_Create", "SportsMedal_Create" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{p};");
        foreach (var t in new[] { "SportsTeams", "SportsEvents", "SportsMedals" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("SportsMedals");
        Delete.Table("SportsEvents");
        Delete.Table("SportsTeams");
    }
}
