using FluentMigrator;

namespace Sms.Migrations;

[Migration(142, "Production blockers: payment-invoice columns, announcement delivery, complaint owner, academic sessions")]
public sealed class M0142_ProductionBlockers : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF COL_LENGTH('dbo.FeePayments', 'InvoiceId') IS NULL
    ALTER TABLE dbo.FeePayments ADD InvoiceId uniqueidentifier NULL;
IF COL_LENGTH('dbo.FeePayments', 'HeadId') IS NULL
    ALTER TABLE dbo.FeePayments ADD HeadId nvarchar(64) NULL;

IF COL_LENGTH('dbo.Announcements', 'RecipientsJson') IS NULL
    ALTER TABLE dbo.Announcements ADD RecipientsJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.Announcements', 'AttachmentFileName') IS NULL
    ALTER TABLE dbo.Announcements ADD AttachmentFileName nvarchar(260) NULL;
IF COL_LENGTH('dbo.Announcements', 'AttachmentContentType') IS NULL
    ALTER TABLE dbo.Announcements ADD AttachmentContentType nvarchar(120) NULL;

IF COL_LENGTH('dbo.Complaints', 'CreatedByUserId') IS NULL
    ALTER TABLE dbo.Complaints ADD CreatedByUserId uniqueidentifier NULL;
""");

        Execute.Sql("""
IF OBJECT_ID(N'dbo.AcademicSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AcademicSessions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AcademicSessions PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        Name nvarchar(40) NOT NULL,
        StartsOn date NULL,
        EndsOn date NULL,
        IsCurrent bit NOT NULL CONSTRAINT DF_AcademicSessions_IsCurrent DEFAULT (0),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_AcademicSessions_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_AcademicSessions_Tenant ON dbo.AcademicSessions (TenantId, IsCurrent);
END
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'AcademicSessionsTenantPolicy')
BEGIN
    EXEC(N'
CREATE SECURITY POLICY rls.AcademicSessionsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AcademicSessions,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AcademicSessions AFTER INSERT
WITH (STATE = ON)');
END
""");

        Execute.Sql("""
IF COL_LENGTH('dbo.Classes', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.Classes ADD AcademicSessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.Subjects', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.Subjects ADD AcademicSessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.TimetableSlots', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.TimetableSlots ADD AcademicSessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.Homework', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.Homework ADD AcademicSessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.Exams', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.Exams ADD AcademicSessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.FeeStructures', 'AcademicSessionId') IS NULL
    ALTER TABLE dbo.FeeStructures ADD AcademicSessionId uniqueidentifier NULL;
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AcademicSessionsTenantPolicy;");
        Execute.Sql("DROP TABLE IF EXISTS dbo.AcademicSessions;");
    }
}
