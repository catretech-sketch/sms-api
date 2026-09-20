using FluentMigrator;

namespace Sms.Migrations;

[Migration(75, "Hostel: Blocks + Rooms + Residents master tables with tenant RLS + insert procs")]
public sealed class M0075_Hostel_Tables : Migration
{
    public override void Up()
    {
        Create.Table("HostelBlocks")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable()
            .WithColumn("Warden").AsString(120).Nullable();
        Create.Index("IX_HostelBlocks_Tenant").OnTable("HostelBlocks").OnColumn("TenantId").Ascending();

        Create.Table("HostelRooms")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BlockId").AsGuid().NotNullable()
            .WithColumn("RoomNo").AsString(40).NotNullable()
            .WithColumn("Capacity").AsInt32().NotNullable().WithDefaultValue(1);
        Create.Index("IX_HostelRooms_Block").OnTable("HostelRooms").OnColumn("BlockId").Ascending();

        Create.Table("HostelResidents")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RoomId").AsGuid().NotNullable()
            .WithColumn("StudentName").AsString(120).NotNullable()
            .WithColumn("StudentId").AsGuid().Nullable();
        Create.Index("IX_HostelResidents_Room").OnTable("HostelResidents").OnColumn("RoomId").Ascending();

        foreach (var t in new[] { "HostelBlocks", "HostelRooms", "HostelResidents" })
            Execute.Sql($@"CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.HostelBlock_Create
    @TenantId uniqueidentifier, @Name nvarchar(80), @Warden nvarchar(120) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.HostelBlocks (TenantId, Name, Warden)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Name, @Warden);
    SELECT Id, TenantId, Name, Warden FROM dbo.HostelBlocks WHERE Id = (SELECT Id FROM @ins);
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.HostelRoom_Create
    @TenantId uniqueidentifier, @BlockId uniqueidentifier, @RoomNo nvarchar(40), @Capacity int = 1
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.HostelRooms (TenantId, BlockId, RoomNo, Capacity)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @BlockId, @RoomNo, @Capacity);
    SELECT r.Id, r.TenantId, r.BlockId,
           (SELECT Name FROM dbo.HostelBlocks WHERE Id = r.BlockId) AS BlockName,
           r.RoomNo, r.Capacity,
           (SELECT COUNT(*) FROM dbo.HostelResidents WHERE RoomId = r.Id) AS Residents
    FROM dbo.HostelRooms r WHERE r.Id = (SELECT Id FROM @ins);
END;");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.HostelResident_Create
    @TenantId uniqueidentifier, @RoomId uniqueidentifier, @StudentName nvarchar(120), @StudentId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.HostelResidents (TenantId, RoomId, StudentName, StudentId)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @RoomId, @StudentName, @StudentId);
    SELECT res.Id, res.TenantId, res.RoomId,
           (SELECT RoomNo FROM dbo.HostelRooms WHERE Id = res.RoomId) AS RoomNo,
           res.StudentName, res.StudentId
    FROM dbo.HostelResidents res WHERE res.Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        foreach (var p in new[] { "HostelBlock_Create", "HostelRoom_Create", "HostelResident_Create" })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{p};");
        foreach (var t in new[] { "HostelBlocks", "HostelRooms", "HostelResidents" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("HostelResidents");
        Delete.Table("HostelRooms");
        Delete.Table("HostelBlocks");
    }
}
