using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1/webhooks")]
[AllowAnonymous]
public sealed class RazorpayFeeWebhookController(
    FeePaymentOrderRepository orders,
    ITenantPaymentCredentialService credentials,
    IRazorpayClient razorpay,
    IFeeService fees,
    ITenantContext tenant,
    ILogger<RazorpayFeeWebhookController> logger) : ApiControllerBase
{
    [HttpPost("razorpay-fees")]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        string orderId;
        string paymentId;
        string eventName;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            eventName = doc.RootElement.GetProperty("event").GetString() ?? "";
            var entity = doc.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            orderId = entity.GetProperty("order_id").GetString() ?? "";
            paymentId = entity.GetProperty("id").GetString() ?? "";
        }
        catch (Exception)
        {
            return Ok(); // malformed payload — nothing we can act on, acknowledge so Razorpay doesn't retry forever
        }

        // order_id is read here ONLY as an untrusted lookup key to find which tenant's secret to
        // try — no state changes happen until the full body's signature verifies below. Nobody
        // knows which tenant this webhook belongs to yet, so this lookup must be able to see rows
        // across every tenant; that requires the same "platform" RLS bypass used by platform-admin
        // flows (dbo.FeePaymentOrders has a tenant-filtered security policy — see
        // rls.fn_tenant_predicate). This is safe: the query is a unique-index lookup on the
        // globally-unique RazorpayOrderId, so it can return at most the one order Razorpay is
        // telling us about, and no state changes happen until the signature verifies below.
        tenant.Set(null, null, isPlatform: true);
        var order = await orders.GetByOrderIdAsync(orderId, ct);
        if (order is null)
            return Ok(); // unknown order — acknowledge, nothing to do, avoid endless retries

        // Now that we know which tenant to check, adopt real (non-platform) tenant context so the
        // credentials lookup below — and everything after it — is RLS-scoped to this one tenant.
        // This is still only a read; the signature itself hasn't verified yet, so no state changes
        // happen until it does below.
        tenant.Set(order.TenantId, null, isPlatform: false);

        var creds = await credentials.GetActiveAsync(order.TenantId, ct);
        if (creds is null || !razorpay.VerifyWebhookSignature(creds.WebhookSecret, rawBody, Request.Headers["X-Razorpay-Signature"].ToString()))
            return BadRequest();

        if (eventName == "payment.failed")
        {
            await orders.MarkStatusAsync(order.Id, "Failed", ct);
            return Ok();
        }
        if (eventName != "payment.captured" && eventName != "order.paid")
            return Ok(); // an event type we don't act on

        var payment = await fees.PayInvoiceAsync(
            order.InvoiceId,
            new PayFeeInvoiceRequest(
                Amount: order.AmountPaise / 100m,
                Method: "Razorpay",
                Mode: null,
                Ref: paymentId,
                StudentName: null, ClassLabel: null, Cls: null, FeeType: null, HeadId: null, HeadName: null,
                IdempotencyKey: RazorpayIdempotency.KeyFor(paymentId)),
            ct);

        if (payment.Error is null)
        {
            await orders.MarkStatusAsync(order.Id, "Captured", ct);
        }
        else
        {
            // Razorpay has already captured this money — if PayInvoiceAsync couldn't record it (e.g.
            // the invoice was fully paid through another path in the interim), the school's books never
            // reflect it. We still ack Razorpay (no retry storm is going to fix a business-logic
            // conflict), but this must not vanish silently: log it so it can be reconciled manually.
            logger.LogError(
                "Razorpay webhook: payment {PaymentId} for order {OrderId} (invoice {InvoiceId}) was " +
                "captured by Razorpay but PayInvoiceAsync failed to record it: {ErrorCode}",
                paymentId, order.RazorpayOrderId, order.InvoiceId, payment.Error.Code);
        }

        return Ok();
    }
}
