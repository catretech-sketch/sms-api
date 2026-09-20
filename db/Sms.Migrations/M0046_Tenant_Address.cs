using FluentMigrator;

namespace Sms.Migrations;

[Migration(46, "Catre: add Tenants.Address; re-apply Client_Create/SetStatus/ChangePlan to return contact + address columns")]
public sealed class M0046_Tenant_Address : Migration
{
    public override void Up()
    {
        Alter.Table("Tenants").AddColumn("Address").AsString(300).Nullable();
        // Re-apply catre client procs inline so already-migrated DBs pick up the new columns.
        // (Cannot use EmbeddedProcs here: migration 6 also reads the same embedded files,
        //  and SQL Server validates column names at proc creation time for existing tables.)
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Country nvarchar(120),
    @ContactName nvarchar(200), @ContactEmail nvarchar(256), @ContactPhone nvarchar(40),
    @Address nvarchar(300), @PlanId uniqueidentifier, @Csm nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @PlanName nvarchar(100), @Tier nvarchar(20), @Mrr decimal(18,2),
            @LS int, @LSt int, @LStor int;

    SELECT @PlanName = p.Name, @Tier = p.Tier, @Mrr = p.Price,
           @LS = p.LimitsStudents, @LSt = p.LimitsStaff, @LStor = p.LimitsStorageGb
    FROM dbo.Plans p WHERE p.Id = @PlanId;

    INSERT dbo.Tenants (Id, Name, Slug, Status, Tier, Country, PlanId, PlanName, Mrr,
        LimitsStudents, LimitsStaff, LimitsStorageGb, ContactName, ContactEmail, ContactPhone, Address, Csm, HealthScore)
    VALUES (@Id, @Name, @Slug, 'trial', @Tier, @Country, @PlanId, @PlanName, ISNULL(@Mrr, 0),
        @LS, @LSt, @LStor, @ContactName, @ContactEmail, @ContactPhone, @Address, @Csm, 100);

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_SetStatus
    @Id uniqueidentifier, @Status nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Tenants SET Status = @Status WHERE Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END");

        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.Client_ChangePlan
    @Id uniqueidentifier, @PlanId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t SET
        t.PlanId = @PlanId, t.PlanName = p.Name, t.Tier = p.Tier, t.Mrr = p.Price,
        t.LimitsStudents = p.LimitsStudents, t.LimitsStaff = p.LimitsStaff, t.LimitsStorageGb = p.LimitsStorageGb
    FROM dbo.Tenants t
    JOIN dbo.Plans p ON p.Id = @PlanId
    WHERE t.Id = @Id;

    SELECT Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb,
           LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore,
           ContactName, ContactEmail, ContactPhone, Address
    FROM dbo.Tenants WHERE Id = @Id;
END");
    }

    public override void Down()
    {
        Delete.Column("Address").FromTable("Tenants");
        // Restore the pre-Address baseline procs (the embedded procs/catre/*.sql files
        // are still at the pre-Address version) so they no longer reference the dropped column.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catre."))
            Execute.Sql(sql);
    }
}
