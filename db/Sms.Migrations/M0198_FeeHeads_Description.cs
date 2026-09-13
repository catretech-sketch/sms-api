using FluentMigrator;

namespace Sms.Migrations;

/// Fee heads gain an optional free-text Description (e.g. "Trip — annual educational trip to
/// Mumbai, Nov 2026"), so parents (and any app reading fee data) see not just a name but what
/// the fee is actually for. Snapshotted onto FeeInvoiceLines at generation time, same as
/// FeeHeadName already is — renaming/redescribing or deleting a fee head later must never
/// change what an already-generated invoice shows.
[Migration(198, "Fees: FeeHeads.Description + snapshot onto FeeInvoiceLines")]
public sealed class M0198_FeeHeads_Description : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.FeeHeads', 'Description') IS NULL
    ALTER TABLE dbo.FeeHeads ADD Description nvarchar(max) NULL;");

        Execute.Sql(@"
IF COL_LENGTH('dbo.FeeInvoiceLines', 'FeeHeadDescription') IS NULL
    ALTER TABLE dbo.FeeInvoiceLines ADD FeeHeadDescription nvarchar(max) NULL;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.FeeHead_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead, Description
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
    @IsTransportFeeHead bit = 0,
    @Description nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.FeeHeads (Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead, Description)
    VALUES (@Id, @TenantId, @Name, @Code, @Active, @IsSystem, @IsTransportFeeHead, @Description);
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead, Description
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
    @IsTransportFeeHead bit = NULL,
    @Description nvarchar(max) = NULL,
    @DescriptionSpecified bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FeeHeads
    SET Name = COALESCE(@Name, Name),
        Code = CASE WHEN @CodeSpecified = 1 THEN @Code ELSE Code END,
        Active = COALESCE(@Active, Active),
        IsTransportFeeHead = COALESCE(@IsTransportFeeHead, IsTransportFeeHead),
        Description = CASE WHEN @DescriptionSpecified = 1 THEN @Description ELSE Description END
    WHERE Id = @Id AND TenantId = @TenantId;
    SELECT Id, TenantId, Name, Code, Active, IsSystem, IsTransportFeeHead, Description
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

        Execute.Sql("IF COL_LENGTH('dbo.FeeInvoiceLines', 'FeeHeadDescription') IS NOT NULL ALTER TABLE dbo.FeeInvoiceLines DROP COLUMN FeeHeadDescription;");
        Execute.Sql("IF COL_LENGTH('dbo.FeeHeads', 'Description') IS NOT NULL ALTER TABLE dbo.FeeHeads DROP COLUMN Description;");
    }
}
