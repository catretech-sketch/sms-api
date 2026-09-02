using FluentMigrator;

namespace Sms.Migrations;

[Migration(165, "Staff documents admin CRUD: StaffDocument_Create/Update procs")]
public sealed class M0165_StaffDocument_CreateUpdate_Procs : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StaffDocument_Create
    @TenantId uniqueidentifier, @StaffId uniqueidentifier, @Label nvarchar(120), @Value nvarchar(200), @Ok bit = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();

    INSERT dbo.StaffDocuments (Id, TenantId, StaffId, Label, Value, Ok, CreatedAt)
    VALUES (@Id, @TenantId, @StaffId, @Label, @Value, @Ok, SYSUTCDATETIME());

    SELECT Id, Label, Value, Ok FROM dbo.StaffDocuments WHERE Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StaffDocument_Update
    @Id uniqueidentifier, @TenantId uniqueidentifier, @StaffId uniqueidentifier,
    @Label nvarchar(120), @Value nvarchar(200), @Ok bit = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Guarded by StaffId + TenantId, not just Id: a document id that exists but belongs to a
    -- different staff member or tenant matches zero rows here, so the SELECT below returns
    -- nothing and the caller sees 404 — same defense-in-depth as Trip_Start's tenant scoping.
    UPDATE dbo.StaffDocuments SET Label = @Label, Value = @Value, Ok = @Ok
    WHERE Id = @Id AND StaffId = @StaffId AND TenantId = @TenantId;

    SELECT Id, Label, Value, Ok FROM dbo.StaffDocuments
    WHERE Id = @Id AND StaffId = @StaffId AND TenantId = @TenantId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StaffDocument_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StaffDocument_Update;");
    }
}
