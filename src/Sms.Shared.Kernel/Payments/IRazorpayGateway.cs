namespace Sms.Shared.Kernel.Payments;

public sealed record RazorpayOrderCreated(string OrderId, long AmountPaise, string Currency);

public interface IRazorpayGateway
{
    bool IsConfigured { get; }
    string KeyId { get; }
    Task<RazorpayOrderCreated> CreateOrderAsync(long amountPaise, string currency, string receipt, CancellationToken ct = default);
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
    bool VerifyWebhookSignature(string body, string signatureHeader);
}
