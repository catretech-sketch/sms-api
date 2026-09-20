namespace Sms.Shared.Kernel.Payments;

public sealed record PaymentResult(bool Success, string Reference, string Method);

/// Cloud-agnostic payment seam. India-first (Razorpay-style) assumption; stub until a provider is named.
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(decimal amount, string currency, CancellationToken ct = default);
}

/// Dev/test stub: always succeeds, returns a fake reference. Swapped for a real provider in Phase 5/6.
public sealed class StubPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(decimal amount, string currency, CancellationToken ct = default) =>
        Task.FromResult(new PaymentResult(true, "stub_" + Guid.NewGuid().ToString("N")[..16], "upi_autopay"));
}
