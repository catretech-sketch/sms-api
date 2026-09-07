using FluentMigrator;

namespace Sms.Migrations;

[Migration(185, "Transport: StudentBusAssignments nullable BusId + RouteId/FeeHeadId for opt-in mapping ahead of bus assignment")]
public sealed class M0185_StudentBusAssignments_TransportMapping : Migration
{
    public override void Up()
    {
        Alter.Column("BusId").OnTable("StudentBusAssignments").AsGuid().Nullable();

        Execute.Sql(@"
IF COL_LENGTH('dbo.StudentBusAssignments', 'RouteId') IS NULL
    ALTER TABLE dbo.StudentBusAssignments ADD RouteId uniqueidentifier NULL;");
        Execute.Sql(@"
IF COL_LENGTH('dbo.StudentBusAssignments', 'FeeHeadId') IS NULL
    ALTER TABLE dbo.StudentBusAssignments ADD FeeHeadId uniqueidentifier NULL;");

        // Distinct from dbo.StudentBus_Assign (legacy per-bus "add a student" flow, unchanged):
        // this proc is the only writer of RouteId/FeeHeadId, called by the new opt-in endpoint.
        // BusId may be NULL here ("Pending Bus Assignment" — route/stop chosen, no bus available yet).
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StudentTransport_Upsert
    @TenantId uniqueidentifier, @StudentId uniqueidentifier,
    @RouteId uniqueidentifier, @StopId uniqueidentifier = NULL,
    @FeeHeadId uniqueidentifier = NULL, @BusId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.StudentBusAssignments AS tgt
    USING (SELECT @TenantId AS TenantId, @StudentId AS StudentId) AS src
        ON tgt.TenantId = src.TenantId AND tgt.StudentId = src.StudentId
    WHEN MATCHED THEN
        UPDATE SET RouteId = @RouteId, StopId = @StopId, FeeHeadId = @FeeHeadId, BusId = @BusId, CreatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (TenantId, StudentId, RouteId, StopId, FeeHeadId, BusId)
        VALUES (@TenantId, @StudentId, @RouteId, @StopId, @FeeHeadId, @BusId);
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StudentTransport_Upsert;");
        Execute.Sql("IF COL_LENGTH('dbo.StudentBusAssignments', 'FeeHeadId') IS NOT NULL ALTER TABLE dbo.StudentBusAssignments DROP COLUMN FeeHeadId;");
        Execute.Sql("IF COL_LENGTH('dbo.StudentBusAssignments', 'RouteId') IS NOT NULL ALTER TABLE dbo.StudentBusAssignments DROP COLUMN RouteId;");
        Alter.Column("BusId").OnTable("StudentBusAssignments").AsGuid().NotNullable();
    }
}
