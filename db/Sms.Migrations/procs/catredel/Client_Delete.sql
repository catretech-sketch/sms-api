CREATE OR ALTER PROCEDURE dbo.Client_Delete
    @Id uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @Id)
    BEGIN
        SELECT CAST(0 AS bit) AS Ok, N'not_found' AS Code,
               CAST(0 AS int) AS Students, CAST(0 AS int) AS Teachers, CAST(0 AS int) AS Staff;
        RETURN;
    END;

    DECLARE @Students int = (SELECT COUNT(*) FROM dbo.Students WHERE TenantId = @Id);
    DECLARE @Teachers int = (SELECT COUNT(*) FROM dbo.Teachers WHERE TenantId = @Id);
    DECLARE @Staff int = (SELECT COUNT(*) FROM dbo.Staff WHERE TenantId = @Id);

    IF (@Students > 0 OR @Teachers > 0 OR @Staff > 0)
    BEGIN
        SELECT CAST(0 AS bit) AS Ok, N'has_people' AS Code,
               @Students AS Students, @Teachers AS Teachers, @Staff AS Staff;
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRAN;

        /* Billing / Catre ops for this school */
        DELETE FROM dbo.PlanUpgradeRequests WHERE TenantId = @Id;
        DELETE FROM dbo.Invoices WHERE TenantId = @Id;
        DELETE FROM dbo.Subscriptions WHERE TenantId = @Id;
        DELETE FROM dbo.OnboardingItems WHERE TenantId = @Id;
        DELETE FROM dbo.AuditLog WHERE TenantId = @Id;

        /* Auth users for the tenant */
        DELETE rt FROM dbo.RefreshTokens rt
            INNER JOIN dbo.Users u ON u.Id = rt.UserId
            WHERE u.TenantId = @Id;
        DELETE ur FROM dbo.UserRoles ur
            INNER JOIN dbo.Users u ON u.Id = ur.UserId
            WHERE u.TenantId = @Id;
        /* UserLogin.UserId is int (legacy) — not linked to dbo.Users.Id */
        DELETE FROM dbo.UserAppSettings WHERE TenantId = @Id;
        DELETE FROM dbo.Users WHERE TenantId = @Id;

        /* Empty operational leftovers (no people, but wizard may have created shells) */
        DELETE FROM dbo.AttendanceRecords WHERE TenantId = @Id;
        DELETE FROM dbo.PeriodAttendanceAudit WHERE TenantId = @Id;
        DELETE FROM dbo.PeriodAttendanceRecords WHERE TenantId = @Id;
        DELETE FROM dbo.ExamPapers WHERE TenantId = @Id;
        DELETE FROM dbo.Exams WHERE TenantId = @Id;
        DELETE FROM dbo.Homework WHERE TenantId = @Id;
        DELETE FROM dbo.Achievements WHERE TenantId = @Id;
        DELETE FROM dbo.Assignments WHERE TenantId = @Id;
        DELETE FROM dbo.FeePayments WHERE TenantId = @Id;
        DELETE FROM dbo.FeeInvoices WHERE TenantId = @Id;
        DELETE FROM dbo.Payslips WHERE TenantId = @Id;
        DELETE FROM dbo.LeaveRequests WHERE TenantId = @Id;
        DELETE FROM dbo.TimetableSlots WHERE TenantId = @Id;
        DELETE FROM dbo.Subjects WHERE TenantId = @Id;
        DELETE FROM dbo.Classes WHERE TenantId = @Id;
        DELETE FROM dbo.Grades WHERE TenantId = @Id;
        DELETE FROM dbo.CalendarEvents WHERE TenantId = @Id;
        DELETE FROM dbo.Announcements WHERE TenantId = @Id;
        DELETE FROM dbo.Notifications WHERE TenantId = @Id;
        DELETE FROM dbo.ChatMessages WHERE TenantId = @Id;
        DELETE FROM dbo.ChatThreads WHERE TenantId = @Id;
        DELETE FROM dbo.Complaints WHERE TenantId = @Id;
        DELETE FROM dbo.Tickets WHERE TenantId = @Id;
        DELETE FROM dbo.CheckIns WHERE TenantId = @Id;
        DELETE FROM dbo.Boardings WHERE TenantId = @Id;
        DELETE FROM dbo.TripPings WHERE TenantId = @Id;
        DELETE FROM dbo.Trips WHERE TenantId = @Id;
        DELETE FROM dbo.BusAssignments WHERE TenantId = @Id;
        DELETE FROM dbo.BusStops WHERE TenantId = @Id;
        DELETE FROM dbo.Buses WHERE TenantId = @Id;
        DELETE FROM dbo.LibraryBooks WHERE TenantId = @Id;
        DELETE FROM dbo.SchoolLocations WHERE TenantId = @Id;

        DELETE FROM dbo.Tenants WHERE Id = @Id;

        COMMIT TRAN;

        SELECT CAST(1 AS bit) AS Ok, N'deleted' AS Code,
               CAST(0 AS int) AS Students, CAST(0 AS int) AS Teachers, CAST(0 AS int) AS Staff;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
