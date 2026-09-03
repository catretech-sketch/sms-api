using FluentMigrator;

namespace Sms.Migrations;

[Migration(169, "Driver profile: Staff license/emergency-contact fields; Bus driver/conductor assignment history")]
public sealed class M0169_DriverProfileFields_AssignmentHistory : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('dbo.Staff', 'LicenseNumber') IS NULL
    ALTER TABLE dbo.Staff ADD LicenseNumber nvarchar(60) NULL;
IF COL_LENGTH('dbo.Staff', 'LicenseExpiry') IS NULL
    ALTER TABLE dbo.Staff ADD LicenseExpiry date NULL;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactName') IS NULL
    ALTER TABLE dbo.Staff ADD EmergencyContactName nvarchar(120) NULL;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactPhone') IS NULL
    ALTER TABLE dbo.Staff ADD EmergencyContactPhone nvarchar(32) NULL;");

        Create.Table("BusDriverAssignments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("StaffId").AsGuid().NotNullable()
            .WithColumn("Role").AsString(10).NotNullable()
            .WithColumn("AssignedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UnassignedAt").AsDateTime2().Nullable()
            .WithColumn("AssignedByUserId").AsGuid().Nullable();
        Create.Index("IX_BusDriverAssignments_Bus_AssignedAt").OnTable("BusDriverAssignments")
            .OnColumn("BusId").Ascending().OnColumn("AssignedAt").Descending();
        Create.Index("IX_BusDriverAssignments_Staff_AssignedAt").OnTable("BusDriverAssignments")
            .OnColumn("StaffId").Ascending().OnColumn("AssignedAt").Descending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.BusDriverAssignmentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusDriverAssignments,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.BusDriverAssignments AFTER INSERT
WITH (STATE = ON);");

        // Bus_Update / Bus_Create re-declared in full (same pattern as M0166): every open
        // driver/conductor assignment row is closed (UnassignedAt) the moment the bus's
        // DriverStaffId/ConductorStaffId column stops pointing at that staff member — whether
        // because it was cleared, reassigned to someone else on this bus, or "stolen" onto a
        // different bus — and a fresh row opened for whoever holds the slot afterward. The
        // Buses.DriverStaffId/ConductorStaffId columns stay the single "who's on it right now"
        // pointer that all existing read paths already use; this table is purely additive history.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Update
    @TenantId uniqueidentifier,
    @BusId uniqueidentifier,
    @BusNo nvarchar(40) = NULL,
    @RouteId uniqueidentifier = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ClearDriver bit = 0,
    @ConductorStaffId uniqueidentifier = NULL,
    @ClearConductor bit = 0,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Buses WHERE Id = @BusId AND TenantId = @TenantId)
        RETURN;

    DECLARE @OldDriverStaffId uniqueidentifier, @OldConductorStaffId uniqueidentifier;
    SELECT @OldDriverStaffId = DriverStaffId, @OldConductorStaffId = ConductorStaffId
    FROM dbo.Buses WHERE Id = @BusId;

    IF @ClearDriver = 1 SET @DriverStaffId = NULL;
    IF @ClearConductor = 1 SET @ConductorStaffId = NULL;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId AND Id <> @BusId);
        IF @StolenFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenFromBusId AND StaffId = @DriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET DriverStaffId = NULL WHERE Id = @StolenFromBusId;
        END

        UPDATE b SET
            b.DriverStaffId = @DriverStaffId,
            b.Driver = s.Name,
            b.DriverPhone = s.Phone
        FROM dbo.Buses b
        INNER JOIN dbo.Staff s ON s.Id = @DriverStaffId AND s.TenantId = @TenantId
        WHERE b.Id = @BusId;

        IF @OldDriverStaffId IS NULL OR @OldDriverStaffId <> @DriverStaffId
        BEGIN
            IF @OldDriverStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                    AND Role = 'driver' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearDriver = 1
    BEGIN
        UPDATE dbo.Buses SET DriverStaffId = NULL, Driver = NULL, DriverPhone = NULL WHERE Id = @BusId;
        IF @OldDriverStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldDriverStaffId
                AND Role = 'driver' AND UnassignedAt IS NULL;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        DECLARE @StolenConductorFromBusId uniqueidentifier =
            (SELECT TOP 1 Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId AND Id <> @BusId);
        IF @StolenConductorFromBusId IS NOT NULL
        BEGIN
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @StolenConductorFromBusId AND StaffId = @ConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
            UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @StolenConductorFromBusId;
        END

        UPDATE dbo.Buses SET ConductorStaffId = @ConductorStaffId WHERE Id = @BusId AND TenantId = @TenantId;

        IF @OldConductorStaffId IS NULL OR @OldConductorStaffId <> @ConductorStaffId
        BEGIN
            IF @OldConductorStaffId IS NOT NULL
                UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                    AND Role = 'conductor' AND UnassignedAt IS NULL;

            INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
            VALUES (NEWID(), @TenantId, @BusId, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);
        END
    END
    ELSE IF @ClearConductor = 1
    BEGIN
        UPDATE dbo.Buses SET ConductorStaffId = NULL WHERE Id = @BusId;
        IF @OldConductorStaffId IS NOT NULL
            UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND BusId = @BusId AND StaffId = @OldConductorStaffId
                AND Role = 'conductor' AND UnassignedAt IS NULL;
    END

    UPDATE b SET
        b.BusNo = COALESCE(@BusNo, b.BusNo),
        b.RouteId = CASE WHEN @RouteId IS NOT NULL THEN @RouteId ELSE b.RouteId END,
        b.RouteName = CASE WHEN @RouteId IS NOT NULL THEN r.Name ELSE b.RouteName END
    FROM dbo.Buses b
    LEFT JOIN dbo.TransportRoutes r ON r.Id = @RouteId AND r.TenantId = @TenantId
    WHERE b.Id = @BusId AND b.TenantId = @TenantId;

    -- Column order here must match UpdatedBusRow's constructor parameter order exactly:
    -- Dapper's fast-path record materializer requires an exact positional match when the
    -- column count equals the constructor's parameter count, so ConductorStaffId (added last
    -- to the C# record to stay source-compatible) must also be selected last here.
    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
        CASE WHEN b.RouteId IS NOT NULL
            THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId)
            ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END AS StopCount,
        (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned,
        b.ConductorStaffId
    FROM dbo.Buses b WHERE b.Id = @BusId;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Bus_Create
    @TenantId uniqueidentifier, @BusNo nvarchar(40),
    @RouteName nvarchar(80) = NULL, @RouteId uniqueidentifier = NULL,
    @Driver nvarchar(120) = NULL, @DriverPhone nvarchar(32) = NULL,
    @DriverStaffId uniqueidentifier = NULL,
    @ConductorStaffId uniqueidentifier = NULL,
    @AssignedByUserId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID(), @ResolvedRouteId uniqueidentifier = @RouteId;

    IF @ResolvedRouteId IS NULL AND @RouteName IS NOT NULL AND LTRIM(RTRIM(@RouteName)) <> ''
        SELECT TOP 1 @ResolvedRouteId = Id FROM dbo.TransportRoutes
        WHERE TenantId = @TenantId AND Name = @RouteName ORDER BY CreatedAt;

    IF @DriverStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'driver' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId);
        UPDATE dbo.Buses SET DriverStaffId = NULL
        WHERE TenantId = @TenantId AND DriverStaffId = @DriverStaffId;

        SELECT @Driver = s.Name, @DriverPhone = s.Phone
        FROM dbo.Staff s WHERE s.Id = @DriverStaffId AND s.TenantId = @TenantId;
    END

    IF @ConductorStaffId IS NOT NULL
    BEGIN
        UPDATE dbo.BusDriverAssignments SET UnassignedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND Role = 'conductor' AND UnassignedAt IS NULL
            AND BusId IN (SELECT Id FROM dbo.Buses WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId);
        UPDATE dbo.Buses SET ConductorStaffId = NULL
        WHERE TenantId = @TenantId AND ConductorStaffId = @ConductorStaffId;
    END

    INSERT dbo.Buses (Id, TenantId, BusNo, RouteName, RouteId, Driver, DriverPhone, DriverStaffId, ConductorStaffId)
    VALUES (@Id, @TenantId, @BusNo, @RouteName, @ResolvedRouteId, @Driver, @DriverPhone, @DriverStaffId, @ConductorStaffId);

    IF @DriverStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @DriverStaffId, 'driver', SYSUTCDATETIME(), @AssignedByUserId);
    IF @ConductorStaffId IS NOT NULL
        INSERT dbo.BusDriverAssignments (Id, TenantId, BusId, StaffId, Role, AssignedAt, AssignedByUserId)
        VALUES (NEWID(), @TenantId, @Id, @ConductorStaffId, 'conductor', SYSUTCDATETIME(), @AssignedByUserId);

    SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.Driver, b.DriverPhone,
        ISNULL((SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = b.RouteId),
               (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id)) AS StopCount,
        0 AS StudentsRiding, 'idle' AS Status, b.ConductorStaffId
    FROM dbo.Buses b WHERE b.Id = @Id;
END");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport.BusDriverAssignments_ListForBus"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingprofile.Staff_GetProfileFields"))
            Execute.Sql(sql);

        // Re-declared in full (same "later migration wins" pattern as Bus_Update/Bus_Create
        // above) purely to add the four new optional params to the existing ISNULL-coalesce
        // update — every other column/behavior is unchanged from procs/staffingpatch/Staff_Update.sql.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Staff_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Role nvarchar(80), @Category nvarchar(40),
    @Department nvarchar(80), @Phone nvarchar(40), @Shift nvarchar(40), @Route nvarchar(80), @Status nvarchar(20),
    @Email nvarchar(256) = NULL, @Gender nvarchar(1) = NULL, @EmployeeCode nvarchar(64) = NULL,
    @LicenseNumber nvarchar(60) = NULL, @LicenseExpiry date = NULL,
    @EmergencyContactName nvarchar(120) = NULL, @EmergencyContactPhone nvarchar(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Staff SET
        Name = ISNULL(@Name, Name),
        Role = ISNULL(@Role, Role),
        Category = ISNULL(@Category, Category),
        Department = ISNULL(@Department, Department),
        Phone = ISNULL(@Phone, Phone),
        Shift = ISNULL(@Shift, Shift),
        Route = ISNULL(@Route, Route),
        Status = ISNULL(@Status, Status),
        Email = ISNULL(@Email, Email),
        Gender = ISNULL(@Gender, Gender),
        EmployeeCode = ISNULL(@EmployeeCode, EmployeeCode),
        LicenseNumber = ISNULL(@LicenseNumber, LicenseNumber),
        LicenseExpiry = ISNULL(@LicenseExpiry, LicenseExpiry),
        EmergencyContactName = ISNULL(@EmergencyContactName, EmergencyContactName),
        EmergencyContactPhone = ISNULL(@EmergencyContactPhone, EmergencyContactPhone)
    WHERE Id = @Id;

    DECLARE @TenantId uniqueidentifier =
        (SELECT TOP 1 TenantId FROM dbo.Staff WHERE Id = @Id);
    IF @TenantId IS NOT NULL
        UPDATE dbo.Tenants
        SET StaffCount = (
            (SELECT COUNT(*) FROM dbo.Teachers te WHERE te.TenantId = @TenantId AND te.Status = N'active')
          + (SELECT COUNT(*) FROM dbo.Staff st WHERE st.TenantId = @TenantId AND st.Status = N'active')
        )
        WHERE Id = @TenantId;

    SELECT Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AttendancePct, Status, AvatarHue, EmployeeCode, Email
    FROM dbo.Staff WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_GetProfileFields;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.BusDriverAssignments_ListForBus;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.BusDriverAssignmentsTenantPolicy;");
        Delete.Table("BusDriverAssignments");

        Execute.Sql(@"
IF COL_LENGTH('dbo.Staff', 'LicenseNumber') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN LicenseNumber;
IF COL_LENGTH('dbo.Staff', 'LicenseExpiry') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN LicenseExpiry;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactName') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN EmergencyContactName;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactPhone') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN EmergencyContactPhone;");
    }
}
