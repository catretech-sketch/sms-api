using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public sealed record RazorpayOrderResponse(string OrderId, long Amount, string Currency, string KeyId, string? PayLink);

public interface IFeeOnlinePaymentService
{
    Task<ApiResult<RazorpayOrderResponse>> CreateOrderAsync(Guid invoiceId, bool initiatedByStaff, CancellationToken ct = default);
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
}
