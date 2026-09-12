using FluentMigrator;

namespace Sms.Migrations;

[Migration(181, "Row-Level Security: tenant predicate on TenantPaymentCredentials + FeePaymentOrders")]
public sealed class M0181_Razorpay_Rls_Policies : Migration
{
    public override void Up()
    {
        // TenantPaymentCredentials: every read/write of this table already runs after tenant context
        // is established (SchoolIntegrationsController is [Authorize]; the webhook/verify paths always
        // resolve order.TenantId and adopt it as tenant context before this table is queried — see
        // RazorpayFeeWebhookController). Same predicate/shape as M0019's FeePaymentsTenantPolicy.
        Execute.Sql(@"
CREATE SECURITY POLICY rls.TenantPaymentCredentialsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TenantPaymentCredentials,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.TenantPaymentCredentials AFTER INSERT
WITH (STATE = ON);");

        // FeePaymentOrders: the webhook's very first read (FeePaymentOrderRepository.GetByOrderIdAsync)
        // deliberately runs BEFORE any tenant is known — it's how the webhook discovers which tenant's
        // secret to try verifying against, keyed only by the untrusted order_id in the payload. There is
        // no tenant to filter by yet at that point, so the ordinary tenant-scoped FILTER/BLOCK predicate
        // would make that lookup return zero rows for every webhook and break the entire payment-capture
        // path. RazorpayFeeWebhookController now explicitly adopts the platform bypass
        // (tenant.Set(null, null, isPlatform: true)) for that one lookup — the same
        // rls.fn_tenant_predicate escape hatch platform-admin flows already use — and immediately
        // switches to the real (non-platform) tenant context once the order (and so its TenantId) is
        // known, before anything else (including the credentials lookup) runs. This is safe because the
        // lookup is a unique-index hit on a globally-unique RazorpayOrderId, so it can only ever return
        // the single order Razorpay's payload is telling us about, and no state changes happen until the
        // webhook signature verifies.
        Execute.Sql(@"
CREATE SECURITY POLICY rls.FeePaymentOrdersTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeePaymentOrders,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.FeePaymentOrders AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.FeePaymentOrdersTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TenantPaymentCredentialsTenantPolicy;");
    }
}
