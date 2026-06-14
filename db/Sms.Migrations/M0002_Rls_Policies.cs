using FluentMigrator;

namespace Sms.Migrations;

[Migration(2, "Row-Level Security: tenant predicate + security policy on tenant-scoped tables")]
public sealed class M0002_Rls_Policies : Migration
{
    public override void Up()
    {
        // Predicate: row visible if it belongs to the SESSION_CONTEXT tenant, OR caller is platform.
        Execute.Sql(@"
CREATE SCHEMA rls;
");
        Execute.Sql(@"
CREATE FUNCTION rls.fn_tenant_predicate(@TenantId uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS allowed
WHERE
    CAST(SESSION_CONTEXT(N'IsPlatform') AS int) = 1
    OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);
");
        Execute.Sql(@"
CREATE SECURITY POLICY rls.UsersTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Users,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Users AFTER INSERT
WITH (STATE = ON);
");
        Execute.Sql(@"
CREATE SECURITY POLICY rls.AuditTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.AuditLog
WITH (STATE = ON);
");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.AuditTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.UsersTenantPolicy;");
        Execute.Sql("DROP FUNCTION IF EXISTS rls.fn_tenant_predicate;");
        Execute.Sql("DROP SCHEMA IF EXISTS rls;");
    }
}
