using FluentMigrator;

namespace Sms.Migrations;

[Migration(164, "Leave: per-requester annual entitlements (balances), tenant RLS, seeded for existing staff/teachers")]
public sealed class M0164_LeaveEntitlements_Table : Migration
{
    public override void Up()
    {
        Create.Table("LeaveEntitlements")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("RequesterId").AsGuid().NotNullable()
            .WithColumn("Type").AsString(20).NotNullable()
            .WithColumn("Year").AsInt32().NotNullable()
            .WithColumn("TotalDays").AsInt32().NotNullable();
        Create.Index("UX_LeaveEntitlements_Requester_Type_Year").OnTable("LeaveEntitlements")
            .OnColumn("TenantId").Ascending()
            .OnColumn("RequesterId").Ascending()
            .OnColumn("Type").Ascending()
            .OnColumn("Year").Ascending()
            .WithOptions().Unique();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.LeaveEntitlementsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LeaveEntitlements,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LeaveEntitlements AFTER INSERT
WITH (STATE = ON);");

        // One-time head start for whoever already has a login today: 12 casual / 8 sick /
        // 15 earned days for the current calendar year. Staff/teachers added after this
        // migration (or in a later year) simply have no row here — GetMyLeaveBalancesAsync
        // treats a missing type as {total:0, used:0} rather than erroring; there's no admin
        // UI to configure entitlements yet, so keeping the fallback honest beats faking a row.
        Execute.Sql(@"
DECLARE @Year int = YEAR(SYSUTCDATETIME());
INSERT INTO dbo.LeaveEntitlements (TenantId, RequesterId, Type, Year, TotalDays)
SELECT DISTINCT u.TenantId, u.UserId, t.Type, @Year, t.TotalDays
FROM (
    SELECT TenantId, UserId FROM dbo.Staff WHERE UserId IS NOT NULL
    UNION
    SELECT TenantId, UserId FROM dbo.Teachers WHERE UserId IS NOT NULL
) u
CROSS JOIN (VALUES ('casual', 12), ('sick', 8), ('earned', 15)) t(Type, TotalDays);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.LeaveEntitlementsTenantPolicy;");
        Delete.Table("LeaveEntitlements");
    }
}
