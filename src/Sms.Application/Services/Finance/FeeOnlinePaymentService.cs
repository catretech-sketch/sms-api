using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public sealed record RazorpayOrderResponse(string OrderId, long Amount, string Currency, string KeyId, string? PayLink);

public sealed record RazorpayVerifyRequest(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);

public interface IFeeOnlinePaymentService
{
    Task<ApiResult<RazorpayOrderResponse>> CreateOrderAsync(Guid invoiceId, bool initiatedByStaff, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> VerifyAsync(Guid invoiceId, RazorpayVerifyRequest req, CancellationToken ct = default);
}

public sealed class FeeOnlinePaymentService(
    IFeeService fees,
    ITenantPaymentCredentialService credentials,
    IRazorpayClient razorpay,
    FeePaymentOrderRepository orders,
    ITenantContext tenant,
    ITenantFeatureSet features) : IFeeOnlinePaymentService
{
    private bool OnlinePaymentAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.OnlineFeePayment);

    public async Task<ApiResult<RazorpayOrderResponse>> CreateOrderAsync(
        Guid invoiceId, bool initiatedByStaff, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!OnlinePaymentAllowed)
            return FeatureGate.Locked<RazorpayOrderResponse>(FeatureCatalog.OnlineFeePayment);

        var inv = await fees.GetInvoiceAsync(invoiceId, ct);
        if (inv is null)
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("not_found", "resource not found"), 404);

        var remaining = Math.Max(0, inv.Amount - inv.PaidAmount);
        if (remaining <= 0 || string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return ApiResult<RazorpayOrderResponse>.Fail(new Error("conflict", "invoice already paid"), 409);

        var creds = await credentials.GetActiveAsync(tid, ct);
        if (creds is null)
            return ApiResult<RazorpayOrderResponse>.Fail(
                new Error("payment_gateway_not_configured", "Online payment is not available for this school yet."), 403);

        var amountPaise = (long)(remaining * 100m);
        var order = await razorpay.CreateOrderAsync(
            creds.KeyId, creds.KeySecret, amountPaise, "INR", $"invoice-{invoiceId:N}", ct);

        var orderRowId = Guid.NewGuid();
        var initiatedBy = initiatedByStaff ? "staff" : "parent";
        await orders.CreateAsync(orderRowId, tid, invoiceId, order.OrderId, amountPaise, initiatedBy, ct);

        string? payLink = null; // Payment-link generation is a small follow-up enhancement; omitted for v1 per the spec's staff-convenience note.

        return ApiResult<RazorpayOrderResponse>.Ok(
            new RazorpayOrderResponse(order.OrderId, order.AmountPaise, order.Currency, creds.KeyId, payLink));
    }

    public async Task<ApiResult<FeePaymentResponse>> VerifyAsync(
        Guid invoiceId, RazorpayVerifyRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var order = await orders.GetByOrderIdAsync(req.RazorpayOrderId, ct);
        if (order is null || order.TenantId != tid || order.InvoiceId != invoiceId)
            return ApiResult<FeePaymentResponse>.Fail(new Error("not_found", "no matching order for this invoice"), 404);

        var creds = await credentials.GetActiveAsync(tid, ct);
        if (creds is null)
            return ApiResult<FeePaymentResponse>.Fail(
                new Error("payment_gateway_not_configured", "Online payment is not available for this school."), 403);

        if (!razorpay.VerifyPaymentSignature(creds.KeySecret, req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature))
            return ApiResult<FeePaymentResponse>.Fail(new Error("invalid_signature", "Payment signature could not be verified"), 400);

        var payment = await fees.PayInvoiceAsync(
            invoiceId,
            new PayFeeInvoiceRequest(
                Amount: order.AmountPaise / 100m,
                Method: "Razorpay",
                Mode: null,
                Ref: req.RazorpayPaymentId,
                StudentName: null, ClassLabel: null, Cls: null, FeeType: null, HeadId: null, HeadName: null,
                IdempotencyKey: DeterministicGuidFrom(req.RazorpayPaymentId)),
            ct);

        if (payment.Error is null)
            await orders.MarkStatusAsync(order.Id, "Captured", ct);

        return payment;
    }

    /// PayFeeInvoiceRequest.IdempotencyKey is a Guid, but Razorpay payment ids are opaque
    /// strings (e.g. "pay_ABC123") — deterministically derive a stable Guid from the payment id
    /// (MD5 of the UTF-8 bytes) so the SAME payment_id always maps to the SAME idempotency key,
    /// which is all RecordInvoicePaymentAsync's uniqueness guarantee actually requires.
    private static Guid DeterministicGuidFrom(string value) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
