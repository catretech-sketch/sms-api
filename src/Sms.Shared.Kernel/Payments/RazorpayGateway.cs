using Microsoft.Extensions.Options;

namespace Sms.Shared.Kernel.Payments;

public sealed class RazorpayGateway(IRazorpayClient client, IOptions<RazorpayOptions> options) : IRazorpayGateway
{
    private readonly RazorpayOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;
    public string KeyId => _opts.KeyId;

    public Task<RazorpayOrderCreated> CreateOrderAsync(
        long amountPaise, string currency, string receipt, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured)
            throw new InvalidOperationException("Razorpay is not configured.");
        return client.CreateOrderAsync(_opts.KeyId, _opts.KeySecret, amountPaise, currency, receipt, ct);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) =>
        RazorpayClient.VerifyPaymentSignature(_opts.KeySecret, orderId, paymentId, signature);

    public bool VerifyWebhookSignature(string body, string signatureHeader) =>
        RazorpayClient.VerifyWebhookSignature(_opts.WebhookSecret, body, signatureHeader);
}
