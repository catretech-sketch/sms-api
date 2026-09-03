using FluentMigrator;

namespace Sms.Migrations;

[Migration(170, "Revert M0169's Staff license/emergency columns — driver profile now reads dbo.PersonExtras (same data the CRM staff editor already writes) instead of a disconnected duplicate")]
public sealed class M0170_Revert_StaffLicenseFields_UsePersonExtras : Migration
{
    public override void Up()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_GetProfileFields;");

        // Staff_Update reverted to its pre-M0169 signature (no LicenseNumber/LicenseExpiry/
        // EmergencyContactName/EmergencyContactPhone params) — identical to
        // procs/staffingpatch/Staff_Update.sql, which M0169 never touched.
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Staff_Update
    @Id uniqueidentifier, @Name nvarchar(200), @Role nvarchar(80), @Category nvarchar(40),
    @Department nvarchar(80), @Phone nvarchar(40), @Shift nvarchar(40), @Route nvarchar(80), @Status nvarchar(20),
    @Email nvarchar(256) = NULL, @Gender nvarchar(1) = NULL, @EmployeeCode nvarchar(64) = NULL
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
        EmployeeCode = ISNULL(@EmployeeCode, EmployeeCode)
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

        Execute.Sql(@"
IF COL_LENGTH('dbo.Staff', 'LicenseNumber') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN LicenseNumber;
IF COL_LENGTH('dbo.Staff', 'LicenseExpiry') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN LicenseExpiry;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactName') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN EmergencyContactName;
IF COL_LENGTH('dbo.Staff', 'EmergencyContactPhone') IS NOT NULL ALTER TABLE dbo.Staff DROP COLUMN EmergencyContactPhone;");
    }

    public override void Down()
    {
        // Not restoring M0169's columns/proc on Down — this migration is itself the correction;
        // rolling back would just reintroduce the disconnected-duplicate problem it fixes.
    }
}
