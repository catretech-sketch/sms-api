using FluentMigrator;

namespace Sms.Migrations;

[Migration(122, "SchoolHouses: tenant house catalog (GET/PUT /v1/houses)")]
public sealed class M0122_SchoolHouses : Migration
{
    public override void Up()
    {
        Create.Table("SchoolHouses")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable();
        Create.UniqueConstraint("UQ_SchoolHouses_Tenant_Name")
            .OnTable("SchoolHouses").Columns("TenantId", "Name");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.SchoolHousesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SchoolHouses,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SchoolHouses AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SchoolHouse_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Name FROM dbo.SchoolHouses ORDER BY Name;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.SchoolHouse_Replace
    @TenantId uniqueidentifier,
    @NamesJson nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.SchoolHouses WHERE TenantId = @TenantId;

    IF @NamesJson IS NOT NULL AND LTRIM(RTRIM(@NamesJson)) NOT IN (N'', N'[]', N'null')
    BEGIN
        INSERT dbo.SchoolHouses (Id, TenantId, Name)
        SELECT NEWID(), @TenantId, LTRIM(RTRIM(j.[value]))
        FROM OPENJSON(@NamesJson) j
        WHERE LTRIM(RTRIM(ISNULL(j.[value], N''))) <> N'';
    END

    SELECT Name FROM dbo.SchoolHouses ORDER BY Name;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.SchoolHouse_Replace;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.SchoolHouse_List;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.SchoolHousesTenantPolicy;");
        Delete.Table("SchoolHouses");
    }
}
