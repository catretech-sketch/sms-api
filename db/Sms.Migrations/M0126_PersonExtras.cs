using FluentMigrator;

namespace Sms.Migrations;

[Migration(126, "PersonExtras JSON store for student/teacher/staff enrolment extras + docs")]
public sealed class M0126_PersonExtras : Migration
{
    public override void Up()
    {
        Create.Table("PersonExtras")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("PersonType").AsString(20).NotNullable() // student | teacher | staff
            .WithColumn("PersonId").AsGuid().NotNullable()
            .WithColumn("ExtrasJson").AsString(int.MaxValue).NotNullable().WithDefaultValue("{}")
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.UniqueConstraint("UQ_PersonExtras_Person")
            .OnTable("PersonExtras").Columns("TenantId", "PersonType", "PersonId");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.PersonExtrasTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PersonExtras,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.PersonExtras AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PersonExtras_Get
  @PersonType nvarchar(20),
  @PersonId uniqueidentifier
AS
BEGIN
  SET NOCOUNT ON;
  SELECT Id, TenantId, PersonType, PersonId, ExtrasJson, UpdatedAt
  FROM dbo.PersonExtras
  WHERE PersonType = @PersonType AND PersonId = @PersonId;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.PersonExtras_Upsert
  @TenantId uniqueidentifier,
  @PersonType nvarchar(20),
  @PersonId uniqueidentifier,
  @ExtrasJson nvarchar(max)
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM dbo.PersonExtras WHERE TenantId = @TenantId AND PersonType = @PersonType AND PersonId = @PersonId)
    UPDATE dbo.PersonExtras SET ExtrasJson = @ExtrasJson, UpdatedAt = SYSUTCDATETIME()
    WHERE TenantId = @TenantId AND PersonType = @PersonType AND PersonId = @PersonId;
  ELSE
    INSERT dbo.PersonExtras (TenantId, PersonType, PersonId, ExtrasJson)
    VALUES (@TenantId, @PersonType, @PersonId, @ExtrasJson);
  SELECT Id, TenantId, PersonType, PersonId, ExtrasJson, UpdatedAt
  FROM dbo.PersonExtras
  WHERE TenantId = @TenantId AND PersonType = @PersonType AND PersonId = @PersonId;
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PersonExtras_Upsert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PersonExtras_Get;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.PersonExtrasTenantPolicy;");
        Delete.Table("PersonExtras");
    }
}
