using FluentMigrator;

namespace Sms.Migrations;

[Migration(47, "Catre: add contact columns to OnboardingItems; apply Onboarding_Create inline (new cols); backfill from linked tenants")]
public sealed class M0047_Onboarding_Contact : Migration
{
    private const string OnboardingCreateInline = @"
CREATE OR ALTER PROCEDURE dbo.Onboarding_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Owner nvarchar(120),
    @Value decimal(18,2), @Stage nvarchar(20),
    @ContactName nvarchar(200) = NULL, @ContactEmail nvarchar(256) = NULL,
    @ContactPhone nvarchar(40) = NULL, @Address nvarchar(300) = NULL,
    @TenantId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.OnboardingItems (Id, TenantId, Name, Slug, Owner, Value, Stage,
        ContactName, ContactEmail, ContactPhone, Address)
    VALUES (@Id, @TenantId, @Name, @Slug, @Owner, ISNULL(@Value, 0), ISNULL(@Stage, 'lead'),
        @ContactName, @ContactEmail, @ContactPhone, @Address);

    INSERT dbo.OnboardingChecklist (OnboardingId, Seq, Label, Done) VALUES
        (@Id, 1, 'Account created', 1), (@Id, 2, 'Admin invited', 0), (@Id, 3, 'Data imported', 0),
        (@Id, 4, 'First login', 0), (@Id, 5, 'Payment set up', 0);

    SELECT @Id AS Id;
END";

    public override void Up()
    {
        Alter.Table("OnboardingItems")
            .AddColumn("ContactName").AsString(200).Nullable()
            .AddColumn("ContactEmail").AsString(256).Nullable()
            .AddColumn("ContactPhone").AsString(40).Nullable()
            .AddColumn("Address").AsString(300).Nullable();

        // Apply the new Onboarding_Create INLINE (the embedded .sql stays at the pre-contact
        // baseline so the historical proc-creation migration still succeeds on fresh installs).
        Execute.Sql(OnboardingCreateInline);

        // Backfill existing tenant-linked cards from their tenant's contact details.
        Execute.Sql(@"
UPDATE o SET o.ContactName = t.ContactName, o.ContactEmail = t.ContactEmail,
             o.ContactPhone = t.ContactPhone, o.Address = t.Address
FROM dbo.OnboardingItems o
JOIN dbo.Tenants t ON t.Id = o.TenantId
WHERE o.TenantId IS NOT NULL;");
    }

    public override void Down()
    {
        Delete.Column("ContactName").Column("ContactEmail").Column("ContactPhone").Column("Address")
            .FromTable("OnboardingItems");
        // Restore the pre-contact baseline Onboarding_Create (embedded .sql is still at baseline)
        // so the proc no longer references the dropped columns.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.catreops."))
            Execute.Sql(sql);
    }
}
