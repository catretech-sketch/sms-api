namespace Sms.Shared.Kernel.Payments;

public sealed record PaymentResult(bool Success, string Reference, string Method);

/// Cloud-agnostic payment seam. India-first (Razorpay-style) assumption; stub until a provider is named.
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(decimal amount, string currency, CancellationToken ct = default);
}

/// Legacy/placeholder gateway: always succeeds, returns a fake reference. No real provider is
/// wired up for this call path yet (real online payments go through IRazorpayGateway with
/// signature verification instead — see FeeOnlinePaymentService). Callers must not expose this
/// path to self-service end users (see FeeController.PayInvoice's staff-only guard on the
/// amount-omitted branch) — it exists for staff to record a payment collected outside the app,
/// not as a substitute for real payment processing.
public sealed class StubPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(decimal amount, string currency, CancellationToken ct = default) =>
        Task.FromResult(new PaymentResult(true, "stub_" + Guid.NewGuid().ToString("N")[..16], "upi_autopay"));
}
