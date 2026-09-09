using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    ITenantContext tenant) : ApiControllerBase
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
        // try — no state changes happen until the full body's signature verifies below.
        var order = await orders.GetByOrderIdAsync(orderId, ct);
        if (order is null)
            return Ok(); // unknown order — acknowledge, nothing to do, avoid endless retries

        var creds = await credentials.GetActiveAsync(order.TenantId, ct);
        if (creds is null || !razorpay.VerifyWebhookSignature(creds.WebhookSecret, rawBody, Request.Headers["X-Razorpay-Signature"].ToString()))
            return BadRequest();

        // This request is [AllowAnonymous] — no JWT/X-Tenant-Id header ran it through
        // TenantResolutionMiddleware, so ITenantContext is still unset here. IFeeService.PayInvoiceAsync
        // (and the RLS-guarded dbo.FeePayments insert it performs) both require ITenantContext.TenantId
        // to be populated, so only NOW — after the webhook signature has verified against this tenant's
        // own secret — do we adopt order.TenantId as the request's tenant context.
        tenant.Set(order.TenantId, null, isPlatform: false);

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
            await orders.MarkStatusAsync(order.Id, "Captured", ct);

        return Ok();
    }
}
