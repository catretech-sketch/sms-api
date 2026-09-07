using FluentMigrator;

namespace Sms.Migrations;

[Migration(184, "Finance: FeeHeads.IsTransportFeeHead flag for Student transport mapping")]
public sealed class M0184_FeeHeads_TransportFlag : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.FeeHeads', 'IsTransportFeeHead') IS NULL
    ALTER TABLE dbo.FeeHeads ADD IsTransportFeeHead bit NOT NULL CONSTRAINT DF_FeeHeads_IsTransportFeeHead DEFAULT (0);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead
    FROM dbo.FeeHeads
    ORDER BY Name;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Create
    @TenantId uniqueidentifier,
    @Name nvarchar(120),
    @Code nvarchar(40) = NULL,
    @Active bit = 1,
    @IsSystem bit = 0,
    @IsTransportFeeHead bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeeHeads (Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead)
    VALUES (@Id, @TenantId, @Name, @Code, @Active, @IsSystem, @IsTransportFeeHead);
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead
    FROM dbo.FeeHeads WHERE Id = @Id;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Update
    @Id uniqueidentifier,
    @TenantId uniqueidentifier,
    @Name nvarchar(120) = NULL,
    @Code nvarchar(40) = NULL,
    @CodeSpecified bit = 0,
    @Active bit = NULL,
    @IsTransportFeeHead bit = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FeeHeads
    SET Name = COALESCE(@Name, Name),
        Code = CASE WHEN @CodeSpecified = 1 THEN @Code ELSE Code END,
        Active = COALESCE(@Active, Active),
        IsTransportFeeHead = COALESCE(@IsTransportFeeHead, IsTransportFeeHead)
    WHERE Id = @Id AND TenantId = @TenantId;
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead
    FROM dbo.FeeHeads WHERE Id = @Id AND TenantId = @TenantId;
END;");
    }

    public override void Down()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads
    ORDER BY Name;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Create
    @TenantId uniqueidentifier,
    @Name nvarchar(120),
    @Code nvarchar(40) = NULL,
    @Active bit = 1,
    @IsSystem bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeeHeads (Id, TenantId, Name, Code, Active, IsSystem)
    VALUES (@Id, @TenantId, @Name, @Code, @Active, @IsSystem);
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads WHERE Id = @Id;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_Update
    @Id uniqueidentifier,
    @TenantId uniqueidentifier,
    @Name nvarchar(120) = NULL,
    @Code nvarchar(40) = NULL,
    @CodeSpecified bit = 0,
    @Active bit = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FeeHeads
    SET Name = COALESCE(@Name, Name),
        Code = CASE WHEN @CodeSpecified = 1 THEN @Code ELSE Code END,
        Active = COALESCE(@Active, Active)
    WHERE Id = @Id AND TenantId = @TenantId;
    SELECT Id, TenantId, Name, Code, Active, IsSystem
    FROM dbo.FeeHeads WHERE Id = @Id AND TenantId = @TenantId;
END;");

        Execute.Sql(@"
IF COL_LENGTH('dbo.FeeHeads', 'IsTransportFeeHead') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FeeHeads DROP CONSTRAINT IF EXISTS DF_FeeHeads_IsTransportFeeHead;
    ALTER TABLE dbo.FeeHeads DROP COLUMN IsTransportFeeHead;
END");
    }
}
