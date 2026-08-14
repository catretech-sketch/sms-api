using FluentMigrator;

namespace Sms.Migrations;

[Migration(125, "Academic periods + class-tests publish snapshots (CRM academics tabs)")]
public sealed class M0125_AcademicPeriods_ClassTests : Migration
{
    public override void Up()
    {
        Create.Table("AcademicPeriodSchedules")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("DraftJson").AsString(int.MaxValue).Nullable()
            .WithColumn("PublishedJson").AsString(int.MaxValue).Nullable()
            .WithColumn("DraftSavedAt").AsDateTime2().Nullable()
            .WithColumn("PublishedAt").AsDateTime2().Nullable();
        Create.UniqueConstraint("UQ_AcademicPeriodSchedules_Tenant")
            .OnTable("AcademicPeriodSchedules").Column("TenantId");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.AcademicPeriodSchedulesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AcademicPeriodSchedules,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AcademicPeriodSchedules AFTER INSERT
WITH (STATE = ON);");

        Create.Table("ClassTestSchedules")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("DraftJson").AsString(int.MaxValue).Nullable()
            .WithColumn("PublishedJson").AsString(int.MaxValue).Nullable()
            .WithColumn("DraftSavedAt").AsDateTime2().Nullable()
            .WithColumn("PublishedAt").AsDateTime2().Nullable();
        Create.UniqueConstraint("UQ_ClassTestSchedules_Tenant")
            .OnTable("ClassTestSchedules").Column("TenantId");

        Execute.Sql(@"
CREATE SECURITY POLICY rls.ClassTestSchedulesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ClassTestSchedules,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.ClassTestSchedules AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AcademicPeriod_Get AS
BEGIN
  SET NOCOUNT ON;
  SELECT TOP 1 Id, TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt
  FROM dbo.AcademicPeriodSchedules;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.AcademicPeriod_Upsert
  @TenantId uniqueidentifier,
  @DraftJson nvarchar(max) = NULL,
  @PublishedJson nvarchar(max) = NULL,
  @DraftSavedAt datetime2 = NULL,
  @PublishedAt datetime2 = NULL
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM dbo.AcademicPeriodSchedules WHERE TenantId = @TenantId)
    UPDATE dbo.AcademicPeriodSchedules SET
      DraftJson = COALESCE(@DraftJson, DraftJson),
      PublishedJson = COALESCE(@PublishedJson, PublishedJson),
      DraftSavedAt = COALESCE(@DraftSavedAt, DraftSavedAt),
      PublishedAt = COALESCE(@PublishedAt, PublishedAt)
    WHERE TenantId = @TenantId;
  ELSE
    INSERT dbo.AcademicPeriodSchedules (TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt)
    VALUES (@TenantId, @DraftJson, @PublishedJson, @DraftSavedAt, @PublishedAt);
  SELECT TOP 1 Id, TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt
  FROM dbo.AcademicPeriodSchedules WHERE TenantId = @TenantId;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ClassTestSchedule_Get AS
BEGIN
  SET NOCOUNT ON;
  SELECT TOP 1 Id, TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt
  FROM dbo.ClassTestSchedules;
END;");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.ClassTestSchedule_Upsert
  @TenantId uniqueidentifier,
  @DraftJson nvarchar(max) = NULL,
  @PublishedJson nvarchar(max) = NULL,
  @DraftSavedAt datetime2 = NULL,
  @PublishedAt datetime2 = NULL
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM dbo.ClassTestSchedules WHERE TenantId = @TenantId)
    UPDATE dbo.ClassTestSchedules SET
      DraftJson = COALESCE(@DraftJson, DraftJson),
      PublishedJson = COALESCE(@PublishedJson, PublishedJson),
      DraftSavedAt = COALESCE(@DraftSavedAt, DraftSavedAt),
      PublishedAt = COALESCE(@PublishedAt, PublishedAt)
    WHERE TenantId = @TenantId;
  ELSE
    INSERT dbo.ClassTestSchedules (TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt)
    VALUES (@TenantId, @DraftJson, @PublishedJson, @DraftSavedAt, @PublishedAt);
  SELECT TOP 1 Id, TenantId, DraftJson, PublishedJson, DraftSavedAt, PublishedAt
  FROM dbo.ClassTestSchedules WHERE TenantId = @TenantId;
END;");
    }

    public override void Down()
    {
        foreach (var p in new[] {
            "ClassTestSchedule_Upsert", "ClassTestSchedule_Get", "AcademicPeriod_Upsert", "AcademicPeriod_Get"
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{p};");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.ClassTestSchedulesTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AcademicPeriodSchedulesTenantPolicy;");
        Delete.Table("ClassTestSchedules");
        Delete.Table("AcademicPeriodSchedules");
    }
}
