using FluentMigrator;

namespace Sms.Migrations;

[Migration(173, "AuditLogs: generic, reusable, insert-only audit trail with tenant RLS")]
public sealed class M0173_AuditLogs_Table : Migration
{
    public override void Up()
    {
        Execute.Sql("""
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TenantId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NULL,
        Action nvarchar(100) NOT NULL,
        Module nvarchar(50) NOT NULL,
        EntityType nvarchar(100) NOT NULL,
        EntityId nvarchar(100) NOT NULL,
        TimestampUtc datetime2 NOT NULL CONSTRAINT DF_AuditLogs_TimestampUtc DEFAULT (SYSUTCDATETIME()),
        BeforeData nvarchar(max) NULL,
        AfterData nvarchar(max) NULL
    );
    CREATE INDEX IX_AuditLogs_Tenant_Entity ON dbo.AuditLogs (TenantId, EntityType, EntityId);
END
""");

        Execute.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'AuditLogsTenantPolicy')
CREATE SECURITY POLICY rls.AuditLogsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLogs,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLogs AFTER INSERT
WITH (STATE = ON);
""");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AuditLogsTenantPolicy;");
        Execute.Sql("DROP TABLE IF EXISTS dbo.AuditLogs;");
    }
}
