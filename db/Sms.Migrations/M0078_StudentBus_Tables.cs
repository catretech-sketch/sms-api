using FluentMigrator;

namespace Sms.Migrations;

[Migration(78, "Transport: StudentBusAssignments (student -> bus) with tenant RLS")]
public sealed class M0078_StudentBus_Tables : Migration
{
    public override void Up()
    {
        Create.Table("StudentBusAssignments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("StopId").AsGuid().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        // One active bus per student, per tenant. Assign is an upsert against this key.
        Create.UniqueConstraint("UQ_StudentBus_Tenant_Student")
            .OnTable("StudentBusAssignments").Columns("TenantId", "StudentId");
        Create.Index("IX_StudentBus_Tenant_Bus").OnTable("StudentBusAssignments")
            .OnColumn("TenantId").Ascending().OnColumn("BusId").Ascending();

        // Tenant isolation: rows visible only to their tenant's session (or platform).
        // BLOCK predicate stops cross-tenant inserts. Mirrors M0023 transport tables.
        Execute.Sql(@"
CREATE SECURITY POLICY rls.StudentBusAssignmentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StudentBusAssignments,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StudentBusAssignments AFTER INSERT
WITH (STATE = ON);");

        // Upsert: a student has exactly one active bus per tenant. The RLS BLOCK predicate rejects
        // any @TenantId that does not match the session's TenantId, so cross-tenant writes cannot occur.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StudentBus_Assign
    @TenantId uniqueidentifier, @StudentId uniqueidentifier,
    @BusId uniqueidentifier, @StopId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.StudentBusAssignments AS tgt
    USING (SELECT @TenantId AS TenantId, @StudentId AS StudentId) AS src
        ON tgt.TenantId = src.TenantId AND tgt.StudentId = src.StudentId
    WHEN MATCHED THEN
        UPDATE SET BusId = @BusId, StopId = @StopId, CreatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (TenantId, StudentId, BusId, StopId)
        VALUES (@TenantId, @StudentId, @BusId, @StopId);
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StudentBus_Unassign
    @TenantId uniqueidentifier, @StudentId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.StudentBusAssignments
    WHERE TenantId = @TenantId AND StudentId = @StudentId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StudentBus_Assign;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StudentBus_Unassign;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.StudentBusAssignmentsTenantPolicy;");
        Delete.Table("StudentBusAssignments");
    }
}
