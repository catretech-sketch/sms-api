using FluentMigrator;

namespace Sms.Migrations;

[Migration(107, "Transport admin: Routes + RouteStops, Buses.RouteId, student opt-out")]
public sealed class M0107_Transport_Routes_Admin : Migration
{
    public override void Up()
    {
        Create.Table("TransportRoutes")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(80).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.UniqueConstraint("UQ_TransportRoutes_Tenant_Name")
            .OnTable("TransportRoutes").Columns("TenantId", "Name");

        Create.Table("RouteStops")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RouteId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(120).NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("Lat").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Lng").AsDouble().NotNullable().WithDefaultValue(0);
        Create.Index("IX_RouteStops_Route_Seq").OnTable("RouteStops")
            .OnColumn("RouteId").Ascending().OnColumn("Seq").Ascending();

        Execute.Sql(@"
IF COL_LENGTH('dbo.Buses', 'RouteId') IS NULL
    ALTER TABLE dbo.Buses ADD RouteId uniqueidentifier NULL;");

        Create.Table("StudentTransportOptOut")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StudentId").AsGuid().NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.UniqueConstraint("UQ_StudentTransportOptOut_Tenant_Student")
            .OnTable("StudentTransportOptOut").Columns("TenantId", "StudentId");

        foreach (var t in new[] { "TransportRoutes", "RouteStops", "StudentTransportOptOut" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.TransportRoute_Create
    @TenantId uniqueidentifier, @Name nvarchar(80), @Stops int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RouteId uniqueidentifier = NEWID();
    INSERT dbo.TransportRoutes (Id, TenantId, Name) VALUES (@RouteId, @TenantId, @Name);

    DECLARE @i int = 1, @n int = CASE WHEN @Stops < 1 THEN 1 WHEN @Stops > 50 THEN 50 ELSE @Stops END;
    WHILE @i <= @n
    BEGIN
        INSERT dbo.RouteStops (Id, TenantId, RouteId, Name, Seq)
        VALUES (NEWID(), @TenantId, @RouteId, CONCAT(N'Stop ', @i), @i);
        SET @i += 1;
    END

    SELECT r.Id, r.Name,
        (SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = r.Id) AS Stops
    FROM dbo.TransportRoutes r WHERE r.Id = @RouteId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40), @RouteName nvarchar(80) = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @RouteId uniqueidentifier = NULL;

    IF @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @RouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @RouteId, @Driver, @DriverPhone);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.Driver, b.DriverPhone,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.BusAssignment_Assign
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @TeacherUserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.BusAssignments AS tgt
    USING (SELECT @TenantId AS TenantId, @TeacherUserId AS TeacherUserId) AS src
        ON tgt.TenantId = src.TenantId AND tgt.TeacherUserId = src.TeacherUserId
    WHEN MATCHED THEN UPDATE SET BusId = @BusId
    WHEN NOT MATCHED THEN
        INSERT (TenantId, TeacherUserId, BusId) VALUES (@TenantId, @TeacherUserId, @BusId);
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.BusAssignment_Unassign
    @TenantId uniqueidentifier, @BusId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.BusAssignments WHERE TenantId = @TenantId AND BusId = @BusId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StudentTransport_OptOut
    @TenantId uniqueidentifier, @StudentId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.StudentTransportOptOut WHERE TenantId = @TenantId AND StudentId = @StudentId)
        INSERT dbo.StudentTransportOptOut (TenantId, StudentId) VALUES (@TenantId, @StudentId);
    DELETE FROM dbo.StudentBusAssignments WHERE TenantId = @TenantId AND StudentId = @StudentId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.StudentTransport_OptIn
    @TenantId uniqueidentifier, @StudentId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.StudentTransportOptOut WHERE TenantId = @TenantId AND StudentId = @StudentId;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StudentTransport_OptIn;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StudentTransport_OptOut;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.BusAssignment_Unassign;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.BusAssignment_Assign;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Bus_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.TransportRoute_Create;");
        foreach (var t in new[] { "StudentTransportOptOut", "RouteStops", "TransportRoutes" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("StudentTransportOptOut");
        Execute.Sql("IF COL_LENGTH('dbo.Buses', 'RouteId') IS NOT NULL ALTER TABLE dbo.Buses DROP COLUMN RouteId;");
        Delete.Table("RouteStops");
        Delete.Table("TransportRoutes");
    }
}
